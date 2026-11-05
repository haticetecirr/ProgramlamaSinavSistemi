using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProgLab.Server.Grading;

/// <summary>
/// Klasik cevapları karşılaştırmaya hazırlar. Aynı adımlar hem cevap anahtarına hem öğrenci cevabına uygulanır.
/// Aşama 4'e ait bir sınıftır ama seed verisindeki normalized_text alanı için Aşama 2'de gerekti.
/// </summary>
public static partial class AnswerNormalizer
{
    private static readonly CultureInfo Turkish = new("tr-TR");

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // 1-2. Satır sonu ve sekmeler dahil tüm boşlukları teke indir, baştan ve sondan sil.
        var result = WhiteSpace().Replace(text, " ").Trim();

        // 3. Sondaki noktalama işaretlerini at (arada kalan boşlukla birlikte).
        result = result.TrimEnd('.', ',', ';', ':', '!', '?', ' ');

        // 4. Türkçe kurallarıyla küçült: I -> ı, İ -> i. Varsayılan kültürle "IŞIK", "ışık" ile eşleşmez.
        result = result.ToLower(Turkish);

        // 5. Aynı harfin farklı kodlanmış hâllerini eşitle.
        return result.Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
