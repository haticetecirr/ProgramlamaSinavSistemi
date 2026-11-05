using ProgLab.Server.Grading;

namespace ProgLab.Server.Tests;

public sealed class AnswerNormalizerTests
{
    [Theory]
    [InlineData("  Yığın. ", "yığın")]            // VT-32
    [InlineData("IŞIK", "ışık")]                  // VT-33 (onaylanan örnek); tr-TR: I -> ı
    [InlineData("İNCİ", "inci")]                  // tr-TR: İ -> i
    [InlineData("bağlı\t\r\n  liste", "bağlı liste")]
    [InlineData("stack ?!", "stack")]
    [InlineData("a.b", "a.b")]                    // yalnızca sondaki noktalama atılır
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normalize_ProducesExpectedText(string? input, string expected)
    {
        Assert.Equal(expected, AnswerNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_UnifiesDifferentlyEncodedLetters()
    {
        const string decomposed = "ş"; // s + birleşen çengel
        const string composed = "ş";    // ş

        Assert.Equal(AnswerNormalizer.Normalize(composed), AnswerNormalizer.Normalize(decomposed));
    }

    /// <summary>
    /// S ile Ş farklı harflerdir; birebir eşleşme kuralı korunur (kullanıcı kararı). Dokümandaki
    /// "ISIK" örneği yerine VT-33 "IŞIK" ile uygulanır. "ISIK" için eğitmen alternatif cevap ekleyebilir.
    /// </summary>
    [Fact]
    public void Normalize_DoesNotTreatSAndSCedillaAsSameLetter()
    {
        Assert.NotEqual(AnswerNormalizer.Normalize("ışık"), AnswerNormalizer.Normalize("ISIK"));
    }
}
