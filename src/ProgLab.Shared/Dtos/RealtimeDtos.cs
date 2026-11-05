using ProgLab.Shared.Enums;

namespace ProgLab.Shared.Dtos;

/// <summary>Soru içeriği taşımaz: yalnızca kimlik ve zaman. Sorular katılımda çekilir.</summary>
public sealed record ExamPublishedEvent(int ExamId, string Title, ExamKind Kind, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc);

/// <param name="Reason">"TimeUp" veya "Manual".</param>
public sealed record ExamEndedEvent(int ExamId, string Reason);

/// <summary>Bağlanan veya ayrılan öğrenci; eğitmen ekranındaki canlı liste için.</summary>
public sealed record StudentConnectionDto(int StudentId, string FullName, string? Ip);

/// <summary>Öğrenci lobisindeki sınav durumu. Sunucu saatinden türetilir.</summary>
public enum StudentExamState
{
    Upcoming = 1,
    Open = 2,
    Submitted = 3,
    Completed = 4
}

/// <summary>Lobideki sınav kartı. Soru veya cevap anahtarı içermez.</summary>
/// <param name="RemainingSeconds">Yalnızca açık sınavda; sunucu saatiyle hesaplanır.</param>
public sealed record StudentExamSummaryDto(
    int Id,
    string Title,
    ExamKind Kind,
    StudentExamState State,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? RemainingSeconds);
