using System.Net;
using Microsoft.EntityFrameworkCore;
using ProgLab.Server.Data;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Logging;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Services;

/// <summary>
/// Sınavın tamamlanmasının tek yolu: süre dolumu ve eğitmenin "Bitir" komutu aynı akışı kullanır.
/// Devam eden gönderimler kayıtlı cevaplarıyla otomatik teslim edilir.
/// </summary>
public sealed class ExamCompletionService(AppDbContext db, ExamNotifier notifier, ActivityRecorder recorder)
{
    /// <summary>Sınav zaten tamamlanmışsa veya başkası tamamladıysa false döner.</summary>
    public async Task<bool> CompleteAsync(int examId, string reason, int? userId, IPAddress? ip, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var exam = await db.Exams.SingleOrDefaultAsync(e => e.Id == examId && e.Status == ExamStatus.Published, ct);
        if (exam is null)
        {
            return false;
        }

        // Aynı anda gelen teslimle çakışmasın: satırlar kilitlenir, sonra güncel hâlleri okunur.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM submissions WHERE exam_id = {examId} FOR UPDATE", ct);
        var inProgress = await db.Submissions
            .Where(s => s.ExamId == examId && s.Status == SubmissionStatus.InProgress)
            .ToListAsync(ct);
        await db.Entry(exam).ReloadAsync(ct);
        if (exam.Status != ExamStatus.Published)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var total = await db.ExamQuestions.CountAsync(q => q.ExamId == examId, ct);
        var ids = inProgress.Select(s => s.Id).ToList();
        var answeredBySubmission = await db.Answers.AsNoTracking()
            .Where(a => ids.Contains(a.SubmissionId) && (a.SelectedOptionId != null || (a.TextAnswer != null && a.TextAnswer.Trim() != "")))
            .GroupBy(a => a.SubmissionId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        foreach (var submission in inProgress)
        {
            var answered = answeredBySubmission.GetValueOrDefault(submission.Id);

            // Puanlama Aşama 9'da burada aynı işlem içinde yapılacak.
            submission.Status = SubmissionStatus.AutoSubmitted;
            submission.SubmittedAt = now;
            recorder.Add(
                ActivityEvents.AutoSubmitted,
                examId,
                submission.StudentId,
                submission,
                null,
                submission.ClientIp,
                new { answeredCount = answered, totalQuestions = total },
                $"{answered}/{total} cevap");
        }

        if (reason == ExamNotifier.ReasonManual)
        {
            (exam.StartsAt, exam.EndsAt) = ExamRules.EarlyEndWindow(exam.StartsAt!.Value, now);
        }

        exam.Status = ExamStatus.Completed;
        exam.CompletedAt = now;
        exam.UpdatedAt = now;

        recorder.Add(
            ActivityEvents.ExamEnded,
            examId,
            userId,
            null,
            null,
            ip,
            new { reason, autoSubmittedCount = inProgress.Count },
            $"{(reason == ExamNotifier.ReasonManual ? "eğitmen bitirdi" : "süre doldu")}, {inProgress.Count} otomatik teslim");

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Süre servisi ile eğitmen aynı anda tamamladı; biri kazanır.
            recorder.Discard();
            return false;
        }

        await transaction.CommitAsync(ct);

        await recorder.WriteFilesAsync(ct);
        await notifier.ExamEndedAsync(exam, reason);
        return true;
    }
}
