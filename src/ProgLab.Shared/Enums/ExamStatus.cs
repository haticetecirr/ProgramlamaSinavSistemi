namespace ProgLab.Shared.Enums;

/// <summary>
/// Sınavın saklanan durumu. "Açık" bir durum değildir; sunucu saatinden türetilir.
/// Sayısal değerler veritabanına yazılır, değiştirilmemelidir.
/// </summary>
public enum ExamStatus : short
{
    Draft = 1,
    Published = 2,
    Completed = 3
}
