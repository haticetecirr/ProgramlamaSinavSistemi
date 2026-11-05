using System.ComponentModel.DataAnnotations;
using ProgLab.Shared.Enums;

namespace ProgLab.Shared.Dtos;

/// <summary>Sınav veya ödev taslağı. Sınavda süre, ödevde başlangıç ve bitiş kullanılır.</summary>
/// <param name="RowVersion">Okunan sürüm; başka pencerede değişmişse kayıt reddedilir. Boşsa kontrol edilmez.</param>
public sealed record SaveExamRequest(
    [Required, MaxLength(200)] string Title,
    string? Description,
    ExamKind Kind,
    int? ClassId,
    int? DurationMinutes,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    uint? RowVersion = null);

/// <param name="Points">Sınava özel puan; boşsa sorunun varsayılan puanı kullanılır.</param>
public sealed record ExamQuestionInput(int QuestionId, short OrderNo, decimal? Points);

public sealed record ExamSummaryDto(
    int Id,
    string Title,
    ExamKind Kind,
    int ClassId,
    string ClassName,
    ExamStatus Status,
    int? DurationMinutes,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int QuestionCount,
    decimal TotalPoints);

public sealed record ExamQuestionDto(
    int Id,
    int QuestionId,
    short OrderNo,
    decimal Points,
    string Title,
    QuestionType Type,
    int VersionNo,
    bool IsCurrent,
    bool IsDeleted);

/// <summary>Sınavın eğitmene özel tam hâli.</summary>
public sealed record InstructorExamDto(
    int Id,
    string Title,
    string? Description,
    ExamKind Kind,
    int ClassId,
    string ClassName,
    ExamStatus Status,
    int? DurationMinutes,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? CompletedAt,
    uint RowVersion,
    decimal TotalPoints,
    IReadOnlyList<ExamQuestionDto> Questions);
