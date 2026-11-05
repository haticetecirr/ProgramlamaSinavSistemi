using ProgLab.Shared.Enums;

namespace ProgLab.Shared.Dtos;

/// <summary>Sonuç listesi satırı. Status boşsa öğrenci sınava katılmamıştır; puan yalnızca teslim edenlerde vardır.</summary>
public sealed record ExamResultRowDto(
    int StudentId,
    string Username,
    string FullName,
    long? SubmissionId,
    SubmissionStatus? Status,
    DateTimeOffset? SubmittedAt,
    int? CorrectCount,
    decimal? TotalScore);

public sealed record ExamResultsDto(
    int ExamId,
    string Title,
    ExamStatus Status,
    int TotalQuestions,
    decimal MaxScore,
    IReadOnlyList<ExamResultRowDto> Rows);

/// <summary>Gönderimdeki bir soru. Yalnızca eğitmene döner: doğru cevabı içerir.</summary>
public sealed record QuestionResultDto(
    int ExamQuestionId,
    short OrderNo,
    QuestionType Type,
    string Text,
    decimal Points,
    long? AnswerId,
    string? AnswerText,
    string CorrectAnswer,
    decimal? Score,
    GradedBy? GradedBy,
    string? GradedNote,
    bool CanOverride);

public sealed record SubmissionDetailDto(
    long SubmissionId,
    int ExamId,
    string ExamTitle,
    string Username,
    string FullName,
    SubmissionStatus Status,
    DateTimeOffset? SubmittedAt,
    decimal? TotalScore,
    decimal MaxScore,
    IReadOnlyList<QuestionResultDto> Questions);

/// <summary>Log paneli satırı; saat UTC gelir, istemci yerel saatte gösterir.</summary>
public sealed record LogEntryDto(
    long Id,
    DateTimeOffset OccurredAt,
    string EventType,
    string Username,
    string FullName,
    string? Ip,
    string Text);
