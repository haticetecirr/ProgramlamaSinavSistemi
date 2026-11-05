using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ProgLab.Server.Data;
using ProgLab.Server.Data.Entities;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Logging;

/// <summary>
/// Sınav olaylarını kaydeder: satır işlemle birlikte activity_logs'a eklenir, dosya satırları
/// işlem kalıcı olduktan sonra yazılır. Böylece geri alınan bir işlem dosyada görünmez.
/// </summary>
public sealed class ActivityRecorder(AppDbContext db, ExamLogWriter writer)
{
    private readonly List<PendingLine> _pending = [];

    public void Add(string eventType, int examId, int? userId, Submission? submission, long? submissionId, IPAddress? ip, object details, string text)
    {
        var now = DateTimeOffset.UtcNow;

        // Satır metni de saklanır: panel ve (kaybolursa) log dosyası tablodan aynen üretilir.
        var json = JsonSerializer.SerializeToNode(details)!.AsObject();
        json["summary"] = text;

        db.ActivityLogs.Add(new ActivityLog
        {
            OccurredAt = now,
            EventType = eventType,
            ExamId = examId,
            UserId = userId,
            Submission = submission,
            SubmissionId = submission is null ? submissionId : null,
            Ip = ip,
            Details = json.ToJsonString()
        });
        _pending.Add(new PendingLine(examId, now, eventType, userId, ip?.ToString(), text));
    }

    /// <summary>Kaydın insan okuyacak metni. Bu alan eklenmeden önce yazılmış kayıtta ham ayrıntı döner.</summary>
    public static string SummaryOf(string? details)
    {
        if (string.IsNullOrEmpty(details))
        {
            return "";
        }

        return JsonNode.Parse(details)?["summary"]?.GetValue<string>() ?? details;
    }

    /// <summary>İşlem geri alındıysa bekleyen dosya satırları da atılır.</summary>
    public void Discard() => _pending.Clear();

    public async Task WriteFilesAsync(CancellationToken ct)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var lines = _pending.ToList();
        _pending.Clear();

        var userIds = lines.Where(l => l.UserId is not null).Select(l => l.UserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.Username, FullName = u.Role == UserRole.Student ? u.FullName : "" }, ct);

        foreach (var line in lines)
        {
            var user = line.UserId is { } id && users.TryGetValue(id, out var found) ? found : null;
            await writer.WriteAsync(
                line.ExamId,
                ExamLogWriter.FormatLine(line.At, line.EventType, user?.Username ?? "", user?.FullName ?? "", line.Ip, line.Text),
                ct);
        }
    }

    private sealed record PendingLine(int ExamId, DateTimeOffset At, string EventType, int? UserId, string? Ip, string Text);
}
