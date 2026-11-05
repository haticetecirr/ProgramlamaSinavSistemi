using System.Net;

namespace ProgLab.Server.Data.Entities;

/// <summary>Sistem olayı; log dosyalarının esas kaynağı.</summary>
public class ActivityLog
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public required string EventType { get; set; }
    public int? UserId { get; set; }
    public int? ExamId { get; set; }
    public long? SubmissionId { get; set; }
    public IPAddress? Ip { get; set; }

    /// <summary>Olaya özgü alanlar (JSON metni, veritabanında jsonb).</summary>
    public string? Details { get; set; }

    public User? User { get; set; }
    public Exam? Exam { get; set; }
    public Submission? Submission { get; set; }
}
