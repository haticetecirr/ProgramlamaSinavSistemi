using ProgLab.Server.Data.Entities;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Grading;

/// <summary>Bir sorunun puanlama sonucu. Negatif puan yoktur.</summary>
public readonly record struct GradeResult(decimal Score)
{
    public static GradeResult Full(decimal points) => new(points);

    public static GradeResult Zero() => new(0m);
}

/// <summary>Her soru tipinin kendi puanlayıcısı vardır. Finalde kod sorusu için yeni bir sınıf eklenir, mevcut kod değişmez.</summary>
public interface IGrader
{
    QuestionType Type { get; }

    GradeResult Grade(ExamQuestion examQuestion, Answer? answer);
}

/// <summary>Doğru seçenek tam puan; yanlış veya boş 0.</summary>
public sealed class MultipleChoiceGrader : IGrader
{
    public QuestionType Type => QuestionType.MultipleChoice;

    public GradeResult Grade(ExamQuestion examQuestion, Answer? answer)
    {
        var correctId = examQuestion.Question!.Options.Single(o => o.IsCorrect).Id;
        return answer?.SelectedOptionId == correctId ? GradeResult.Full(examQuestion.Points) : GradeResult.Zero();
    }
}

/// <summary>
/// Metin karşılaştırmasıdır, cevabın anlamını değerlendirmez: normalize cevap kabul edilenlerden
/// biriyle birebir aynıysa tam puan, değilse 0. Farklı ifade edilmiş doğru cevabı eğitmen elle düzeltir.
/// </summary>
public sealed class ClassicGrader : IGrader
{
    public QuestionType Type => QuestionType.Classic;

    public GradeResult Grade(ExamQuestion examQuestion, Answer? answer)
    {
        var normalized = AnswerNormalizer.Normalize(answer?.TextAnswer);
        return normalized.Length > 0 && examQuestion.Question!.AnswerKeys.Any(k => k.NormalizedText == normalized)
            ? GradeResult.Full(examQuestion.Points)
            : GradeResult.Zero();
    }
}
