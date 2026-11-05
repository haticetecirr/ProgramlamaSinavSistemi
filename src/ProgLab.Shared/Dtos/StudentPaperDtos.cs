using ProgLab.Shared.Enums;

namespace ProgLab.Shared.Dtos;

/// <summary>Katılma sonucu. Aynı öğrenci tekrar katılırsa aynı gönderim döner; süre sunucu saatiyle hesaplanır.</summary>
public sealed record JoinExamResponse(long SubmissionId, SubmissionStatus Status, int RemainingSeconds, DateTimeOffset EndsAtUtc);

/// <summary>Öğrenciye giden seçenek. Doğru bilgisi yoktur.</summary>
public sealed record StudentOptionDto(int Id, short OrderNo, string Text);

/// <summary>
/// Öğrenciye giden soru. Eğitmen tipinden ayrı bir tiptir: doğru seçenek ve kabul edilen cevap alanı
/// bu tipte hiç bulunmadığı için yanlışlıkla gönderilemez (VT-38).
/// </summary>
public sealed record StudentQuestionDto(
    int ExamQuestionId,
    short OrderNo,
    QuestionType Type,
    string Text,
    decimal Points,
    IReadOnlyList<StudentOptionDto> Options);

/// <summary>Öğrencinin daha önce kaydedilmiş cevabı. Puan bilgisi içermez.</summary>
public sealed record StudentAnswerDto(int ExamQuestionId, int? SelectedOptionId, string? TextAnswer);

/// <summary>Sınav kâğıdı: sorular ve kayıtlı cevaplar.</summary>
public sealed record StudentPaperDto(
    int ExamId,
    long SubmissionId,
    string Title,
    string? Description,
    ExamKind Kind,
    int RemainingSeconds,
    DateTimeOffset EndsAtUtc,
    IReadOnlyList<StudentQuestionDto> Questions,
    IReadOnlyList<StudentAnswerDto> Answers);

/// <summary>Otomatik kayıt. Çoktan seçmelide seçenek (boş = işaret kaldırıldı), klasikte metin.</summary>
public sealed record SaveAnswerRequest(int? SelectedOptionId, string? TextAnswer);

/// <summary>Teslim sonucu. Zaman sunucu saatidir.</summary>
/// <summary>Elle puan düzeltme isteği.</summary>
public sealed record OverrideScoreRequest(decimal Score, string? Note);

/// <summary>Düzeltilen puan ve gönderimin yeni toplamı.</summary>
public sealed record OverrideScoreResponse(decimal Score, decimal TotalScore);

public sealed record SubmitResponse(DateTimeOffset SubmittedAt, int AnsweredCount);

/// <summary>Sunucunun kalan süreyi 30 saniyede bir bildirmesi; istemcideki sayaç bununla düzeltilir.</summary>
public sealed record TimeSyncEvent(int ExamId, int RemainingSeconds);
