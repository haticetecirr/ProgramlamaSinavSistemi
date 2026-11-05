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
/// <param name="HasStarted">Öğrenci daha önce katıldı mı? Düğme "Katıl" yerine "Devam et" olur.</param>
/// <param name="CanContinue">Şu an girilebilir mi? Teslim edilmiş ödeve bitişe kadar yeniden girilebilir.</param>
public sealed record StudentExamSummaryDto(
    int Id,
    string Title,
    ExamKind Kind,
    StudentExamState State,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? RemainingSeconds,
    bool HasStarted,
    bool CanContinue);

public sealed record AnswerProgressEvent(int ExamId, int StudentId, int AnsweredCount);

public sealed record SubmissionReceivedEvent(int ExamId, int StudentId, DateTimeOffset SubmittedAt, decimal? TotalScore);

public sealed record SecondDeviceEvent(int ExamId, int StudentId, string FullName, string? Ip1, string? Ip2);

public sealed record LiveStudentDto(
    int StudentId,
    string Username,
    string FullName,
    bool IsConnected,
    string? Ip,
    int AnsweredCount,
    SubmissionStatus? Status,
    DateTimeOffset? SubmittedAt);

/// <summary>Eğitmenin canlı izleme ekranı. Doğru cevap veya puan içermez.</summary>
public sealed record LiveExamDto(
    int ExamId,
    string Title,
    ExamStatus Status,
    int RemainingSeconds,
    DateTimeOffset EndsAt,
    int TotalQuestions,
    IReadOnlyList<LiveStudentDto> Students,
    IReadOnlyList<string> Warnings);
