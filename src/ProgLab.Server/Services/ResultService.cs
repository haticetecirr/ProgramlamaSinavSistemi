using Microsoft.EntityFrameworkCore;
using ProgLab.Server.Data;
using ProgLab.Server.Errors;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Services;

/// <summary>Eğitmenin sonuç ekranları. Şubede olup katılmayan öğrenci de listede yer alır.</summary>
public sealed class ResultService(AppDbContext db)
{
    public async Task<ExamResultsDto> GetResultsAsync(int examId, CancellationToken ct)
    {
        var exam = await db.Exams.AsNoTracking().SingleOrDefaultAsync(e => e.Id == examId && e.Status != ExamStatus.Draft, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.ExamNotFound, "Sınav bulunamadı.");

        var points = await db.ExamQuestions.AsNoTracking().Where(q => q.ExamId == examId).Select(q => q.Points).ToListAsync(ct);

        var submissions = await db.Submissions.AsNoTracking()
            .Where(s => s.ExamId == examId)
            .Select(s => new
            {
                s.Id,
                s.StudentId,
                s.Status,
                s.SubmittedAt,
                s.TotalScore,
                Correct = s.Answers.Count(a => a.Score != null && a.Score == a.ExamQuestion!.Points)
            })
            .ToDictionaryAsync(s => s.StudentId, ct);

        // Sonradan şubeden çıkarılan öğrencinin gönderimi de görünür.
        var studentIds = submissions.Keys.ToList();
        var students = await db.Users.AsNoTracking()
            .Where(u => studentIds.Contains(u.Id) || db.ClassMembers.Any(m => m.ClassId == exam.ClassId && m.StudentId == u.Id))
            .OrderBy(u => u.Username)
            .Select(u => new { u.Id, u.Username, u.FullName })
            .ToListAsync(ct);

        var rows = students.Select(u =>
        {
            var submission = submissions.GetValueOrDefault(u.Id);
            var isDone = submission is not null && submission.Status != SubmissionStatus.InProgress;
            return new ExamResultRowDto(
                u.Id,
                u.Username,
                u.FullName,
                submission?.Id,
                submission?.Status,
                isDone ? submission!.SubmittedAt : null,
                isDone ? submission!.Correct : null,
                isDone ? submission!.TotalScore : null);
        }).ToList();

        return new ExamResultsDto(exam.Id, exam.Title, exam.Status, points.Count, points.Sum(), rows);
    }

    public async Task<SubmissionDetailDto> GetSubmissionAsync(long submissionId, CancellationToken ct)
    {
        var submission = await db.Submissions.AsNoTracking()
            .Include(s => s.Student)
            .Include(s => s.Exam)
            .Include(s => s.Answers)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.SubmissionNotFound, "Gönderim bulunamadı.");

        // Sınav kilitli soru sürümüne bağlıdır: gösterilen metin ve anahtar puanlananla aynıdır.
        var questions = await db.ExamQuestions.AsNoTracking()
            .Where(q => q.ExamId == submission.ExamId)
            .OrderBy(q => q.OrderNo)
            .Include(q => q.Question).ThenInclude(q => q!.Options)
            .Include(q => q.Question).ThenInclude(q => q!.AnswerKeys)
            .AsSplitQuery()
            .ToListAsync(ct);

        var items = questions.Select(q =>
        {
            var answer = submission.Answers.SingleOrDefault(a => a.ExamQuestionId == q.Id);
            var question = q.Question!;
            var isClassic = question.Type == QuestionType.Classic;

            string? OptionLabel(int? id) => question.Options.FirstOrDefault(o => o.Id == id) is { } option
                ? $"{(char)('A' + option.OrderNo - 1)}) {option.Text}"
                : null;

            return new QuestionResultDto(
                q.Id,
                q.OrderNo,
                question.Type,
                question.Text,
                q.Points,
                answer?.Id,
                isClassic ? answer?.TextAnswer : OptionLabel(answer?.SelectedOptionId),
                isClassic
                    ? string.Join(" / ", question.AnswerKeys.Select(k => k.AcceptedText))
                    : OptionLabel(question.Options.Single(o => o.IsCorrect).Id)!,
                answer?.Score,
                answer?.GradedBy,
                answer?.GradedNote,
                isClassic && answer is not null && submission.Status != SubmissionStatus.InProgress);
        }).ToList();

        return new SubmissionDetailDto(
            submission.Id,
            submission.ExamId,
            submission.Exam!.Title,
            submission.Student!.Username,
            submission.Student.FullName,
            submission.Status,
            submission.Status == SubmissionStatus.InProgress ? null : submission.SubmittedAt,
            submission.Status == SubmissionStatus.InProgress ? null : submission.TotalScore,
            questions.Sum(q => q.Points),
            items);
    }
}
