using ProgLab.Server.Errors;
using ProgLab.Server.Grading;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Services;

/// <summary>Veritabanına gitmeden yapılan soru kuralları; saf olduğu için birim testle doğrulanır.</summary>
public static class QuestionValidator
{
    public const int MinOptions = 2;
    public const int MaxOptions = 6;
    public const int MaxAnswerKeyLength = 500;
    public const decimal MaxPoints = 9999.99m; // numeric(6,2) üst sınırı

    public static void Validate(SaveQuestionRequest request)
    {
        if (request.DefaultPoints <= 0 || request.DefaultPoints > MaxPoints)
        {
            throw Unprocessable(ErrorCodes.InvalidPoints, "Puan 0'dan büyük olmalıdır.");
        }

        switch (request.Type)
        {
            case QuestionType.MultipleChoice:
                ValidateMultipleChoice(request.Options ?? []);
                break;
            case QuestionType.Classic:
                _ = NormalizeAnswerKeys(request.AcceptedAnswers ?? []);
                break;
            default:
                // Coding (3) dahil: kod soruları final kapsamındadır.
                throw Unprocessable(ErrorCodes.UnsupportedQuestionType, "Bu soru tipi desteklenmiyor.");
        }
    }

    /// <summary>Boş satırları atar, her kabul edilen cevabı normalize eder ve tekrarları reddeder.</summary>
    public static IReadOnlyList<(string Accepted, string Normalized)> NormalizeAnswerKeys(IReadOnlyList<string> accepted)
    {
        var keys = accepted
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => (Accepted: a.Trim(), Normalized: AnswerNormalizer.Normalize(a)))
            .ToList();

        // Yalnızca noktalamadan oluşan cevap normalize edilince boş kalır; o da geçersizdir.
        if (keys.Count == 0 || keys.Any(k => k.Normalized.Length == 0))
        {
            throw Unprocessable(ErrorCodes.ClassicNoAnswerKey, "Klasik soruda en az bir kabul edilen cevap olmalıdır.");
        }

        if (keys.Any(k => k.Accepted.Length > MaxAnswerKeyLength))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest, "Kabul edilen cevap en fazla 500 karakter olabilir.");
        }

        if (keys.Select(k => k.Normalized).Distinct().Count() != keys.Count)
        {
            throw Unprocessable(ErrorCodes.DuplicateAnswerKey, "Aynı kabul edilen cevap iki kez yazılmış.");
        }

        return keys;
    }

    private static void ValidateMultipleChoice(IReadOnlyList<QuestionOptionInput> options)
    {
        if (options.Count is < MinOptions or > MaxOptions)
        {
            throw Unprocessable(ErrorCodes.McqOptionCount, "Çoktan seçmeli soruda 2 ile 6 arasında seçenek olmalıdır.");
        }

        var correctCount = options.Count(o => o.IsCorrect);
        if (correctCount == 0)
        {
            throw Unprocessable(ErrorCodes.McqNoCorrectOption, "Doğru seçeneği işaretleyin.");
        }

        if (correctCount > 1)
        {
            throw Unprocessable(ErrorCodes.McqMultipleCorrect, "Yalnızca bir doğru seçenek olabilir.");
        }
    }

    private static ApiException Unprocessable(string code, string title) =>
        new(StatusCodes.Status422UnprocessableEntity, code, title);
}
