using System.Net;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Data.Entities;

/// <summary>Bir öğrencinin bir sınavdaki gönderimi. Yeniden bağlanan öğrenci aynı satıra döner.</summary>
public class Submission
{
    public long Id { get; set; }
    public int ExamId { get; set; }
    public int StudentId { get; set; }
    public SubmissionStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? LastSavedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public int SubmitCount { get; set; }
    public decimal? TotalScore { get; set; }
    public DateTimeOffset? GradedAt { get; set; }
    public IPAddress? ClientIp { get; set; }

    public Exam? Exam { get; set; }
    public User? Student { get; set; }
    public List<Answer> Answers { get; set; } = [];
}
