namespace ProgLab.Shared.Enums;

/// <summary>Gönderim durumu. Sayısal değerler veritabanına yazılır, değiştirilmemelidir.</summary>
public enum SubmissionStatus : short
{
    InProgress = 1,
    Submitted = 2,
    AutoSubmitted = 3
}
