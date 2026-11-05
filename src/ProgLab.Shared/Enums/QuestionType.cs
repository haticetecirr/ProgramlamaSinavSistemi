namespace ProgLab.Shared.Enums;

/// <summary>Soru tipi. Sayısal değerler veritabanına yazılır, değiştirilmemelidir.</summary>
public enum QuestionType : short
{
    MultipleChoice = 1,
    Classic = 2,

    /// <summary>[FİNAL] Vizede tanımlıdır ama kullanılmaz; servisler bu tipi reddeder.</summary>
    Coding = 3
}
