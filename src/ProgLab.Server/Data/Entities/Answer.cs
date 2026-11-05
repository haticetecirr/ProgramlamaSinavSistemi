using ProgLab.Shared.Enums;

namespace ProgLab.Server.Data.Entities;

/// <summary>Gönderimdeki bir sorunun cevabı.</summary>
public class Answer
{
    public long Id { get; set; }
    public long SubmissionId { get; set; }
    public int ExamQuestionId { get; set; }

    /// <summary>Çoktan seçmeli soru için seçilen seçenek.</summary>
    public int? SelectedOptionId { get; set; }

    /// <summary>Klasik soru için yazılan metin.</summary>
    public string? TextAnswer { get; set; }

    public decimal? Score { get; set; }
    public GradedBy? GradedBy { get; set; }
    public string? GradedNote { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Submission? Submission { get; set; }
    public ExamQuestion? ExamQuestion { get; set; }
    public QuestionOption? SelectedOption { get; set; }
}
