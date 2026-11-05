namespace ProgLab.Server.Data.Entities;

/// <summary>Çoktan seçmeli soru seçeneği.</summary>
public class QuestionOption
{
    public int Id { get; set; }
    public int QuestionId { get; set; }

    /// <summary>A, B, C… sırası.</summary>
    public short OrderNo { get; set; }

    public required string Text { get; set; }
    public bool IsCorrect { get; set; }

    public Question? Question { get; set; }
}
