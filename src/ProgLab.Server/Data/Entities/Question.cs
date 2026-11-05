using ProgLab.Shared.Enums;

namespace ProgLab.Server.Data.Entities;

/// <summary>Soru bankası. Her satır bir sorunun bir sürümüdür.</summary>
public class Question
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public QuestionType Type { get; set; }
    public required string Title { get; set; }
    public required string Text { get; set; }
    public decimal DefaultPoints { get; set; }
    public int VersionNo { get; set; } = 1;
    public int? PreviousVersionId { get; set; }

    /// <summary>Bankada listelenen sürüm mü?</summary>
    public bool IsCurrent { get; set; } = true;

    /// <summary>Yayınlanmış bir sınavda kullanıldı; içeriği artık değişemez.</summary>
    public bool IsLocked { get; set; }

    public bool IsDeleted { get; set; }
    public int CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Category? Category { get; set; }
    public Question? PreviousVersion { get; set; }
    public User? Creator { get; set; }
    public List<QuestionOption> Options { get; set; } = [];
    public List<ClassicAnswerKey> AnswerKeys { get; set; } = [];
}
