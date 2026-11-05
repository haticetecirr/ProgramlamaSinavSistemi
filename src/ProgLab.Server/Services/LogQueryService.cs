using Microsoft.EntityFrameworkCore;
using ProgLab.Server.Data;
using ProgLab.Server.Errors;
using ProgLab.Server.Logging;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Services;

/// <summary>Log paneli ve ham dosya. Esas kaynak activity_logs tablosudur; dosya yoksa tablodan üretilir.</summary>
public sealed class LogQueryService(AppDbContext db, ExamLogWriter writer)
{
    private const int PageSize = 100;

    public async Task<PagedResult<LogEntryDto>> ListAsync(int examId, string? eventType, string? username, DateTimeOffset? from, DateTimeOffset? to, int page, CancellationToken ct)
    {
        await EnsureExamExistsAsync(examId, ct);

        var query = Logs(examId);
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(l => l.EventType == eventType);
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            // Puan düzeltme satırının sahibi eğitmendir; öğrencinin numarasıyla da bulunur.
            var text = username.Trim();
            query = query.Where(l => l.User!.Username.Contains(text) || l.Submission!.Student!.Username.Contains(text));
        }

        if (from is { } start)
        {
            query = query.Where(l => l.OccurredAt >= start.ToUniversalTime());
        }

        if (to is { } end)
        {
            query = query.Where(l => l.OccurredAt <= end.ToUniversalTime());
        }

        page = Math.Max(page, 1);
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(l => l.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(Row())
            .ToListAsync(ct);

        return new PagedResult<LogEntryDto>(rows.Select(ToDto).ToList(), total, page, PageSize);
    }

    public async Task<string> GetFileAsync(int examId, CancellationToken ct)
    {
        await EnsureExamExistsAsync(examId, ct);
        return await writer.ReadOrCreateAsync(examId, BuildFileLinesAsync, ct);
    }

    private async Task<IReadOnlyList<string>> BuildFileLinesAsync(int examId, CancellationToken ct)
    {
        var rows = await Logs(examId).OrderBy(l => l.Id).Select(Row()).ToListAsync(ct);
        return rows
            .Select(r => ExamLogWriter.FormatLine(r.OccurredAt, r.EventType, r.Username ?? "", DisplayName(r), r.Ip?.ToString(), ActivityRecorder.SummaryOf(r.Details)))
            .ToList();
    }

    private IQueryable<Data.Entities.ActivityLog> Logs(int examId) =>
        db.ActivityLogs.AsNoTracking().Where(l => l.ExamId == examId);

    private static System.Linq.Expressions.Expression<Func<Data.Entities.ActivityLog, LogRow>> Row() => l =>
        new LogRow(l.Id, l.OccurredAt, l.EventType, l.User!.Username, l.User.FullName, l.User.Role, l.Ip, l.Details);

    private static LogEntryDto ToDto(LogRow row) =>
        new(row.Id, row.OccurredAt, row.EventType, row.Username ?? "", DisplayName(row), row.Ip?.ToString(), ActivityRecorder.SummaryOf(row.Details));

    /// <summary>Dosya biçimiyle aynı: eğitmen satırında ad boştur.</summary>
    private static string DisplayName(LogRow row) => row.Role == UserRole.Student ? row.FullName ?? "" : "";

    private async Task EnsureExamExistsAsync(int examId, CancellationToken ct)
    {
        if (!await db.Exams.AnyAsync(e => e.Id == examId, ct))
        {
            throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.ExamNotFound, "Sınav bulunamadı.");
        }
    }

    private sealed record LogRow(long Id, DateTimeOffset OccurredAt, string EventType, string? Username, string? FullName, UserRole? Role, System.Net.IPAddress? Ip, string? Details);
}
