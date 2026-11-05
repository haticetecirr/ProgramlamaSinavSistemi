using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using ProgLab.Server.Data;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Errors;
using ProgLab.Server.Grading;
using ProgLab.Server.Logging;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Services;

/// <summary>Öğrencinin sınava katılması, sınav kâğıdı, cevap kaydı ve teslim.</summary>
public sealed class SubmissionService(AppDbContext db, ExamNotifier notifier, ActivityRecorder recorder, GradingService grading)
{
    public const int MaxAnswerLength = 2000;

    // Öğrenci cevabını değiştirirse eski puan (eğitmenin elle verdiği dahil) o cevaba ait değildir, silinir.
    private const string AnswerChanged =
        "answers.selected_option_id IS DISTINCT FROM EXCLUDED.selected_option_id OR answers.text_answer IS DISTINCT FROM EXCLUDED.text_answer";

    /// <summary>
    /// Gönderim yoksa oluşturur, varsa aynısını döner. Süre öğrenciye değil sınava bağlı olduğu için
    /// uygulamayı kapatıp açmak süreyi sıfırlamaz.
    /// </summary>
    public async Task<JoinExamResponse> JoinAsync(int examId, int studentId, IPAddress? ip, CancellationToken ct)
    {
        var exam = await FindVisibleExamAsync(examId, studentId, ct);
        var now = DateTimeOffset.UtcNow;
        EnsureOpen(exam, now);

        var submission = await db.Submissions.SingleOrDefaultAsync(s => s.ExamId == examId && s.StudentId == studentId, ct);
        EnsureCanContinue(exam, submission);

        var isNew = submission is null;
        var previousIp = submission?.ClientIp;
        if (submission is null)
        {
            submission = new Submission
            {
                ExamId = examId,
                StudentId = studentId,
                Status = SubmissionStatus.InProgress,
                StartedAt = now
            };
            db.Submissions.Add(submission);
        }

        submission.ClientIp = ip;
        AddJoinedLogs(submission, examId, studentId, ip, previousIp, isNew);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı öğrenci aynı anda iki kez katıldı: benzersiz indeks tek gönderim bırakır, ötekine onu döneriz.
            db.ChangeTracker.Clear();
            recorder.Discard();
            submission = await db.Submissions.SingleAsync(s => s.ExamId == examId && s.StudentId == studentId, ct);
            AddJoinedLogs(submission, examId, studentId, ip, previousIp: null, isNew: false);
            await db.SaveChangesAsync(ct);
        }

        await recorder.WriteFilesAsync(ct);
        await notifier.AddStudentToExamAsync(studentId, examId);

        // Canlı izleme ekranı katılımı bu olayla görür.
        if (isNew)
        {
            await notifier.AnswerProgressAsync(new AnswerProgressEvent(examId, studentId, 0));
        }

        // Çökme sonrası dönüş için ikinci cihaz engellenmez, yalnızca eğitmen uyarılır.
        if (IsSecondDevice(previousIp, ip))
        {
            var fullName = await db.Users.AsNoTracking().Where(u => u.Id == studentId).Select(u => u.FullName).SingleAsync(ct);
            await notifier.SecondDeviceAsync(new SecondDeviceEvent(examId, studentId, fullName, previousIp!.ToString(), ip!.ToString()));
        }

        return new JoinExamResponse(submission.Id, submission.Status, ExamRules.RemainingSeconds(exam.EndsAt!.Value, now), exam.EndsAt.Value);
    }

    /// <summary>Sorular ve kayıtlı cevaplar. Yalnızca sınav açıkken ve öğrenci katıldıysa döner.</summary>
    public async Task<StudentPaperDto> GetPaperAsync(int examId, int studentId, CancellationToken ct)
    {
        var exam = await FindVisibleExamAsync(examId, studentId, ct);
        var now = DateTimeOffset.UtcNow;
        EnsureOpen(exam, now);

        var submission = await db.Submissions.AsNoTracking()
            .Include(s => s.Answers)
            .SingleOrDefaultAsync(s => s.ExamId == examId && s.StudentId == studentId, ct)
            ?? throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.InvalidState, "Önce sınava katılmalısınız.");
        EnsureCanContinue(exam, submission);

        // Puan ve içerik sınavdaki (kilitli) sürümden gelir; bankadaki yeni sürüm bu sınavı etkilemez.
        var questions = await db.ExamQuestions.AsNoTracking()
            .Where(q => q.ExamId == examId)
            .OrderBy(q => q.OrderNo)
            .Select(q => new StudentQuestionDto(
                q.Id,
                q.OrderNo,
                q.Question!.Type,
                q.Question.Text,
                q.Points,
                q.Question.Options
                    .OrderBy(o => o.OrderNo)
                    .Select(o => new StudentOptionDto(o.Id, o.OrderNo, o.Text))
                    .ToList()))
            .ToListAsync(ct);

        return new StudentPaperDto(
            exam.Id,
            submission.Id,
            exam.Title,
            exam.Description,
            exam.Kind,
            ExamRules.RemainingSeconds(exam.EndsAt!.Value, now),
            exam.EndsAt.Value,
            questions,
            submission.Answers.Select(a => new StudentAnswerDto(a.ExamQuestionId, a.SelectedOptionId, a.TextAnswer)).ToList());
    }

    /// <summary>
    /// Otomatik kayıt. Aynı cevabın tekrar gönderilmesi aynı sonucu verir (ON CONFLICT).
    /// Gönderim satırı kilitlenir: aynı anda gelen kayıt, teslim ve süre sonu birbirini ezmez.
    /// </summary>
    public async Task SaveAnswerAsync(long submissionId, int examQuestionId, SaveAnswerRequest request, int studentId, IPAddress? ip, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var (submission, exam) = await LockOwnedSubmissionAsync(submissionId, studentId, ct);
        var now = DateTimeOffset.UtcNow;

        // Süre sonrası tolerans yoktur; istemcinin zamanı kullanılmaz.
        if (!exam.IsOpenAt(now))
        {
            await RejectAsync(ActivityEvents.SaveRejected, exam.Id, submission, studentId, ip, StatusCodes.Status409Conflict, ErrorCodes.ExamClosed, "Sınav süresi doldu.", ct);
        }

        if (exam.Kind == ExamKind.Exam && submission.Status != SubmissionStatus.InProgress)
        {
            await RejectAsync(ActivityEvents.SaveRejected, exam.Id, submission, studentId, ip, StatusCodes.Status409Conflict, ErrorCodes.AlreadySubmitted, "Bu sınavı zaten teslim ettiniz.", ct);
        }

        var examQuestion = await db.ExamQuestions.AsNoTracking()
            .Include(q => q.Question)
            .SingleOrDefaultAsync(q => q.Id == examQuestionId && q.ExamId == exam.Id, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.QuestionNotFound, "Soru bu sınavda bulunamadı.");

        int? optionId = null;
        string? text = null;
        if (examQuestion.Question!.Type == QuestionType.MultipleChoice)
        {
            optionId = request.SelectedOptionId;
            if (optionId is not null && !await db.QuestionOptions.AnyAsync(o => o.Id == optionId && o.QuestionId == examQuestion.QuestionId, ct))
            {
                throw new ApiException(StatusCodes.Status422UnprocessableEntity, ErrorCodes.OptionNotInQuestion, "Seçenek bu soruya ait değil.");
            }
        }
        else
        {
            text = request.TextAnswer;
            if (text?.Length > MaxAnswerLength)
            {
                throw new ApiException(StatusCodes.Status422UnprocessableEntity, ErrorCodes.AnswerTooLong, "Cevap en fazla 2000 karakter olabilir.");
            }
        }

        await db.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO answers (submission_id, exam_question_id, selected_option_id, text_answer, updated_at)
            VALUES (@sid, @eq, @option, @text, @now)
            ON CONFLICT (submission_id, exam_question_id)
            DO UPDATE SET
                selected_option_id = EXCLUDED.selected_option_id,
                text_answer = EXCLUDED.text_answer,
                updated_at = EXCLUDED.updated_at,
                score = CASE WHEN {AnswerChanged} THEN NULL ELSE answers.score END,
                graded_by = CASE WHEN {AnswerChanged} THEN NULL ELSE answers.graded_by END,
                graded_note = CASE WHEN {AnswerChanged} THEN NULL ELSE answers.graded_note END
            """,
            [
                new NpgsqlParameter("sid", submission.Id),
                new NpgsqlParameter("eq", examQuestionId),
                new NpgsqlParameter("option", NpgsqlDbType.Integer) { Value = (object?)optionId ?? DBNull.Value },
                new NpgsqlParameter("text", NpgsqlDbType.Varchar) { Value = (object?)text ?? DBNull.Value },
                new NpgsqlParameter("now", now)
            ],
            ct);

        submission.LastSavedAt = now;

        // Teslim edilmiş ödev yeniden düzenlendi: yeni teslim gelmezse bitişte en son kayıt otomatik teslim edilir.
        if (submission.Status == SubmissionStatus.Submitted)
        {
            submission.Status = SubmissionStatus.InProgress;
            submission.TotalScore = null;
            submission.GradedAt = null;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var answered = await CountAnsweredAsync(submission.Id, ct);
        await notifier.AnswerProgressAsync(new AnswerProgressEvent(exam.Id, studentId, answered));
    }

    /// <summary>Teslim. Sınavda bir kez; ödevde bitişe kadar tekrar edilebilir, her teslim loglanır.</summary>
    public async Task<SubmitResponse> SubmitAsync(long submissionId, int studentId, IPAddress? ip, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var (submission, exam) = await LockOwnedSubmissionAsync(submissionId, studentId, ct);
        var now = DateTimeOffset.UtcNow;

        if (!exam.IsOpenAt(now))
        {
            await RejectAsync(ActivityEvents.SubmitRejected, exam.Id, submission, studentId, ip, StatusCodes.Status409Conflict, ErrorCodes.ExamClosed, "Sınav süresi doldu.", ct);
        }

        if (exam.Kind == ExamKind.Exam && submission.Status != SubmissionStatus.InProgress)
        {
            await RejectAsync(ActivityEvents.SubmitRejected, exam.Id, submission, studentId, ip, StatusCodes.Status409Conflict, ErrorCodes.AlreadySubmitted, "Bu sınavı zaten teslim ettiniz.", ct);
        }

        var answers = await db.Answers.AsNoTracking()
            .Where(a => a.SubmissionId == submission.Id)
            .Select(a => new { a.ExamQuestionId, a.SelectedOptionId, a.TextAnswer })
            .ToListAsync(ct);
        var answered = answers.Count(a => a.SelectedOptionId is not null || !string.IsNullOrWhiteSpace(a.TextAnswer));
        var total = await db.ExamQuestions.CountAsync(q => q.ExamId == exam.Id, ct);

        submission.Status = SubmissionStatus.Submitted;
        submission.SubmittedAt = now;
        submission.SubmitCount++;
        submission.ClientIp = ip ?? submission.ClientIp;

        var totalScore = await grading.GradeSubmissionAsync(submission, ct);

        recorder.Add(
            ActivityEvents.Submitted,
            exam.Id,
            studentId,
            submission,
            null,
            ip,
            new { answeredCount = answered, totalQuestions = total, totalScore, submitNo = submission.SubmitCount, answersSummary = answers },
            $"{answered}/{total} cevap, {ExamLogWriter.FormatScore(totalScore)} puan, {submission.SubmitCount}. teslim");

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await recorder.WriteFilesAsync(ct);
        await notifier.SubmissionReceivedAsync(new SubmissionReceivedEvent(exam.Id, studentId, now, submission.TotalScore));

        return new SubmitResponse(now, answered);
    }

    private Task<int> CountAnsweredAsync(long submissionId, CancellationToken ct) =>
        db.Answers.CountAsync(a => a.SubmissionId == submissionId && (a.SelectedOptionId != null || (a.TextAnswer != null && a.TextAnswer.Trim() != "")), ct);

    private async Task<(Submission Submission, Exam Exam)> LockOwnedSubmissionAsync(long submissionId, int studentId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM submissions WHERE id = {submissionId} FOR UPDATE", ct);

        var submission = await db.Submissions.SingleOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.SubmissionNotFound, "Gönderim bulunamadı.");

        if (submission.StudentId != studentId)
        {
            throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.NotOwner, "Bu gönderim size ait değil.");
        }

        var exam = await db.Exams.AsNoTracking().SingleAsync(e => e.Id == submission.ExamId, ct);
        return (submission, exam);
    }

    /// <summary>Reddedilen istek loglanır: işlem geri alınır, log ayrı kaydedilir, hata fırlatılır.</summary>
    private async Task RejectAsync(string eventType, int examId, Submission submission, int studentId, IPAddress? ip, int status, string code, string title, CancellationToken ct)
    {
        var submissionId = submission.Id;
        await db.Database.RollbackTransactionAsync(ct);
        db.ChangeTracker.Clear();
        recorder.Discard();

        recorder.Add(eventType, examId, studentId, null, submissionId, ip, new { code }, code);
        await db.SaveChangesAsync(ct);
        await recorder.WriteFilesAsync(ct);

        throw new ApiException(status, code, title);
    }

    /// <summary>Taslak öğrenciye hiç görünmez; başka şubenin sınavı reddedilir.</summary>
    private async Task<Exam> FindVisibleExamAsync(int examId, int studentId, CancellationToken ct)
    {
        var exam = await db.Exams.AsNoTracking().SingleOrDefaultAsync(e => e.Id == examId && e.Status != ExamStatus.Draft, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.ExamNotFound, "Sınav bulunamadı.");

        if (!await db.ClassMembers.AnyAsync(m => m.ClassId == exam.ClassId && m.StudentId == studentId, ct))
        {
            throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.NotInClass, "Bu sınav sizin şubenize açık değil.");
        }

        return exam;
    }

    private static void EnsureOpen(Exam exam, DateTimeOffset now)
    {
        if (exam.IsOpenAt(now))
        {
            return;
        }

        if (exam.Status == ExamStatus.Published && now < exam.StartsAt)
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.ExamNotOpen, "Sınav henüz başlamadı.");
        }

        throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.ExamClosed, "Sınav süresi doldu.");
    }

    /// <summary>Sınav türünde teslimden sonra geri dönülmez; ödevde bitişe kadar dönülebilir.</summary>
    private static void EnsureCanContinue(Exam exam, Submission? submission)
    {
        if (exam.Kind == ExamKind.Exam && submission is not null && submission.Status != SubmissionStatus.InProgress)
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.AlreadySubmitted, "Bu sınavı zaten teslim ettiniz.");
        }
    }

    private static bool IsSecondDevice(IPAddress? previous, IPAddress? current) =>
        previous is not null && current is not null && !previous.Equals(current);

    private void AddJoinedLogs(Submission submission, int examId, int studentId, IPAddress? ip, IPAddress? previousIp, bool isNew)
    {
        recorder.Add(ActivityEvents.Joined, examId, studentId, submission, null, ip, new { isNew }, isNew ? "yeni gönderim" : "devam etti");

        if (IsSecondDevice(previousIp, ip))
        {
            recorder.Add(
                ActivityEvents.SecondDevice,
                examId,
                studentId,
                submission,
                null,
                ip,
                new { ip1 = previousIp!.ToString(), ip2 = ip!.ToString() },
                $"önceki adres {previousIp}");
        }
    }
}
