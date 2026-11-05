using System.ComponentModel.DataAnnotations;
using ProgLab.Shared.Enums;

namespace ProgLab.Shared.Dtos;

/// <summary>Kategori ağacındaki bir düğüm.</summary>
public sealed record CategoryDto(int Id, string Name, int? ParentId, IReadOnlyList<CategoryDto> Children);

public sealed record SaveCategoryRequest([Required, MaxLength(100)] string Name, int? ParentId);

public sealed record QuestionOptionInput([Required, MaxLength(500)] string Text, bool IsCorrect);

/// <summary>Soru ekleme ve güncelleme. MCQ'da Options, klasikte AcceptedAnswers kullanılır.</summary>
public sealed record SaveQuestionRequest(
    int CategoryId,
    QuestionType Type,
    [Required, MaxLength(200)] string Title,
    [Required] string Text,
    decimal DefaultPoints,
    IReadOnlyList<QuestionOptionInput>? Options,
    IReadOnlyList<string>? AcceptedAnswers);

/// <summary>Soru bankası listesindeki satır.</summary>
public sealed record QuestionSummaryDto(
    int Id,
    int CategoryId,
    string CategoryName,
    QuestionType Type,
    string Title,
    decimal DefaultPoints,
    int VersionNo,
    bool IsLocked);

public sealed record InstructorOptionDto(int Id, short OrderNo, string Text, bool IsCorrect);

public sealed record InstructorAnswerKeyDto(int Id, string AcceptedText);

/// <summary>
/// Sorunun eğitmene özel tam hâli: doğru seçeneği ve kabul edilen cevapları içerir.
/// Bu tip öğrenciye asla gönderilmez; öğrenci için ayrı StudentQuestionDto yazılacak (Aşama 7).
/// </summary>
public sealed record InstructorQuestionDto(
    int Id,
    int CategoryId,
    QuestionType Type,
    string Title,
    string Text,
    decimal DefaultPoints,
    int VersionNo,
    int? PreviousVersionId,
    bool IsCurrent,
    bool IsLocked,
    bool IsDeleted,
    IReadOnlyList<InstructorOptionDto> Options,
    IReadOnlyList<InstructorAnswerKeyDto> AnswerKeys);

/// <param name="NewVersionId">Kilitli soru düzenlendiyse oluşan yeni sürümün kimliği; yerinde güncellemede boştur.</param>
/// <param name="Question">Sorunun güncel hâli.</param>
public sealed record UpdateQuestionResponse(int? NewVersionId, InstructorQuestionDto Question);
