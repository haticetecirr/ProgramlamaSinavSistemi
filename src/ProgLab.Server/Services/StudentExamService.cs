using Microsoft.EntityFrameworkCore;
using ProgLab.Server.Data;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Services;

/// <summary>Öğrencinin gördüğü sınavlar. Katılma ve cevaplama Aşama 7'de eklenir.</summary>
public sealed class StudentExamService(AppDbContext db)
{
    /// <summary>Yalnızca öğrencinin üyesi olduğu şubelerin yayınlanmış ve tamamlanmış sınavları; taslak asla görünmez.</summary>
    public async Task<IReadOnlyList<StudentExamSummaryDto>> GetLobbyAsync(int studentId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var exams = await db.Exams.AsNoTracking()
            .Where(e => e.Status != ExamStatus.Draft)
            .Where(e => db.ClassMembers.Any(m => m.ClassId == e.ClassId && m.StudentId == studentId))
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.Kind,
                e.Status,
                StartsAt = e.StartsAt!.Value,
                EndsAt = e.EndsAt!.Value,
                SubmissionStatus = db.Submissions
                    .Where(s => s.ExamId == e.Id && s.StudentId == studentId)
                    .Select(s => (SubmissionStatus?)s.Status)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return exams
            .Select(e =>
            {
                var state = ExamRules.StudentState(e.Status, e.StartsAt, e.EndsAt, e.SubmissionStatus, now);
                return new StudentExamSummaryDto(
                    e.Id,
                    e.Title,
                    e.Kind,
                    state,
                    e.StartsAt,
                    e.EndsAt,
                    state == StudentExamState.Open ? ExamRules.RemainingSeconds(e.EndsAt, now) : null,
                    e.SubmissionStatus is not null);
            })
            .OrderBy(e => e.State)
            .ThenBy(e => e.State == StudentExamState.Completed ? -e.EndsAt.ToUnixTimeSeconds() : e.EndsAt.ToUnixTimeSeconds())
            .ToList();
    }
}
