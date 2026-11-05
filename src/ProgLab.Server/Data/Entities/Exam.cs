using ProgLab.Shared.Enums;

namespace ProgLab.Server.Data.Entities;

/// <summary>Sınav veya ödev.</summary>
public class Exam
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public ExamKind Kind { get; set; }
    public int ClassId { get; set; }
    public int? DurationMinutes { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public ExamStatus Status { get; set; } = ExamStatus.Draft;
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>[İsteğe Bağlı Geliştirme] Öğrenciye sonuç gösterimi için ayrıldı.</summary>
    public DateTimeOffset? ResultsPublishedAt { get; set; }

    public int CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>PostgreSQL'in xmin sistem kolonu: aynı sınavın aynı anda iki pencereden düzenlenmesini yakalar.</summary>
    public uint RowVersion { get; set; }

    public SchoolClass? Class { get; set; }
    public User? Creator { get; set; }
    public List<ExamQuestion> Questions { get; set; } = [];
}
