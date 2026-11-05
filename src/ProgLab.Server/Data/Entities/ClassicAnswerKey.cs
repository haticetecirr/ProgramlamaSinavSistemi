namespace ProgLab.Server.Data.Entities;

/// <summary>Klasik sorunun kabul edilen cevabı.</summary>
public class ClassicAnswerKey
{
    public int Id { get; set; }
    public int QuestionId { get; set; }

    /// <summary>Eğitmenin yazdığı hâli.</summary>
    public required string AcceptedText { get; set; }

    /// <summary>AnswerNormalizer çıktısı; karşılaştırma bununla yapılır.</summary>
    public required string NormalizedText { get; set; }

    public Question? Question { get; set; }
}
