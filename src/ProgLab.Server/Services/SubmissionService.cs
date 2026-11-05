using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProgLab.Server.Data;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Errors;
using ProgLab.Server.Logging;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Services;

/// <summary>Öğrencinin sınava katılması ve sınav kâğıdı. Cevap kaydı ve teslim Aşama 8'de eklenir.</summary>
public sealed class SubmissionService(AppDbContext db, ExamNotifier notifier)
{
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
        AddJoinedLog(submission, examId, studentId, ip, isNew);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı öğrenci aynı anda iki kez katıldı: benzersiz indeks tek gönderim bırakır, ötekine onu döneriz.
            db.ChangeTracker.Clear();
            submission = await db.Submissions.SingleAsync(s => s.ExamId == examId && s.StudentId == studentId, ct);
            AddJoinedLog(submission, examId, studentId, ip, isNew: false);
            await db.SaveChangesAsync(ct);
        }

        await notifier.AddStudentToExamAsync(studentId, examId);

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

    private void AddJoinedLog(Submission submission, int examId, int studentId, IPAddress? ip, bool isNew) =>
        db.ActivityLogs.Add(new ActivityLog
        {
            EventType = ActivityEvents.Joined,
            ExamId = examId,
            UserId = studentId,
            Submission = submission,
            Ip = ip,
            Details = JsonSerializer.Serialize(new { isNew })
        });
}
