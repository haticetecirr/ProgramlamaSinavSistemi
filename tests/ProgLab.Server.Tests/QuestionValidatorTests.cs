using ProgLab.Server.Errors;
using ProgLab.Server.Services;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>Soru doğrulama kuralları (VT-11'in birim kısmı). Veritabanı gerekmez.</summary>
public sealed class QuestionValidatorTests
{
    private static QuestionOptionInput[] Options(int count, int correct = 1) =>
        Enumerable.Range(1, count).Select(i => new QuestionOptionInput($"Seçenek {i}", i <= correct)).ToArray();

    private static SaveQuestionRequest Mcq(QuestionOptionInput[] options, decimal points = 10) =>
        new(1, QuestionType.MultipleChoice, "Başlık", "Metin", points, options, null);

    private static SaveQuestionRequest Classic(params string[] answers) =>
        new(1, QuestionType.Classic, "Başlık", "Metin", 10, null, answers);

    private static void AssertRejected(SaveQuestionRequest request, string code, int status = 422)
    {
        var error = Assert.Throws<ApiException>(() => QuestionValidator.Validate(request));
        Assert.Equal(code, error.Code);
        Assert.Equal(status, error.StatusCode);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void Mcq_WithValidOptionCount_IsAccepted(int count) => QuestionValidator.Validate(Mcq(Options(count)));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void Mcq_WithInvalidOptionCount_IsRejected(int count) => AssertRejected(Mcq(Options(count)), ErrorCodes.McqOptionCount);

    [Fact]
    public void Mcq_WithoutOptionsList_IsRejected() =>
        AssertRejected(new SaveQuestionRequest(1, QuestionType.MultipleChoice, "B", "M", 10, null, null), ErrorCodes.McqOptionCount);

    [Fact]
    public void VT11_Mcq_WithoutCorrectOption_IsRejected() => AssertRejected(Mcq(Options(4, correct: 0)), ErrorCodes.McqNoCorrectOption);

    [Fact]
    public void Mcq_WithTwoCorrectOptions_IsRejected() => AssertRejected(Mcq(Options(4, correct: 2)), ErrorCodes.McqMultipleCorrect);

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10000")]
    public void InvalidPoints_AreRejected(string points) =>
        AssertRejected(Mcq(Options(4), decimal.Parse(points)), ErrorCodes.InvalidPoints);

    [Fact]
    public void Classic_WithAlternatives_IsAccepted() => QuestionValidator.Validate(Classic("yığın", "stack", "yığıt"));

    [Fact]
    public void Classic_WithoutAnswers_IsRejected()
    {
        AssertRejected(Classic(), ErrorCodes.ClassicNoAnswerKey);
        AssertRejected(Classic("   ", ""), ErrorCodes.ClassicNoAnswerKey);
        AssertRejected(new SaveQuestionRequest(1, QuestionType.Classic, "B", "M", 10, null, null), ErrorCodes.ClassicNoAnswerKey);
    }

    [Fact]
    public void Classic_WithOnlyPunctuation_IsRejected() => AssertRejected(Classic("..."), ErrorCodes.ClassicNoAnswerKey);

    [Theory]
    [InlineData("yığın", "YIĞIN")]
    [InlineData("yığın", "  yığın. ")]
    [InlineData("bağlı liste", "bağlı   liste!")]
    public void Classic_WithSameNormalizedAnswer_IsRejected(string first, string second) =>
        AssertRejected(Classic(first, second), ErrorCodes.DuplicateAnswerKey);

    [Fact]
    public void Classic_WithTooLongAnswer_IsRejected() =>
        AssertRejected(Classic(new string('a', 501)), ErrorCodes.InvalidRequest, 400);

    [Fact]
    public void NormalizeAnswerKeys_DropsBlanks_AndKeepsOriginalText()
    {
        var keys = QuestionValidator.NormalizeAnswerKeys(["  Yığın. ", "", "STACK"]);

        Assert.Equal(2, keys.Count);
        Assert.Equal(("Yığın.", "yığın"), keys[0]);
        Assert.Equal(("STACK", "stack"), keys[1]);
    }

    [Fact]
    public void CodingQuestion_IsRejected_InMidtermVersion()
    {
        var request = new SaveQuestionRequest(1, QuestionType.Coding, "Kod", "Metin", 10, null, null);

        AssertRejected(request, ErrorCodes.UnsupportedQuestionType);
    }

    [Fact]
    public void UnknownQuestionType_IsRejected()
    {
        var request = new SaveQuestionRequest(1, (QuestionType)99, "B", "M", 10, null, null);

        AssertRejected(request, ErrorCodes.UnsupportedQuestionType);
    }
}
