using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace ProgLab.Server.Logging;

/// <summary>
/// Sınav başına bir metin log dosyası yazar: logs/exams/exam-&lt;id&gt;.log. Dosya başına tek kilit olduğu için
/// 60 öğrenci aynı anda teslim etse de satırlar karışmaz. Esas kaynak activity_logs tablosudur:
/// dosyaya yazılamazsa işlem durmaz, yalnızca uygulama loguna hata düşer.
/// </summary>
public sealed class ExamLogWriter(IConfiguration configuration, IWebHostEnvironment environment, ILogger<ExamLogWriter> logger)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ConcurrentDictionary<int, SemaphoreSlim> _gates = new();

    /// <summary>Boşsa dosyaya yazılmaz: test ortamı yalnızca istendiğinde yazar.</summary>
    private string? Directory => configuration["ExamLogs:Directory"] is { Length: > 0 } configured
        ? configured
        : environment.IsEnvironment("Testing") ? null : Path.Combine(environment.ContentRootPath, "logs", "exams");

    /// <summary>Log dosyasında puan her zaman iki ondalıklı ve noktalıdır: 72.50</summary>
    public static string FormatScore(decimal score) => score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    public static string FormatLine(DateTimeOffset at, string eventType, string username, string fullName, string? ip, string text) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{at.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff zzz} | {eventType,-15} | {username,-10} | {fullName,-15} | {ip,-15} | {text}");

    public string? GetPath(int examId) => Directory is { } folder ? Path.Combine(folder, $"exam-{examId}.log") : null;

    /// <summary>Ham dosyayı okur. Dosya yoksa verilen satırlarla (tablodan) yeniden üretir ve kaydeder.</summary>
    public async Task<string> ReadOrCreateAsync(int examId, Func<int, CancellationToken, Task<IReadOnlyList<string>>> buildLines, CancellationToken ct)
    {
        if (GetPath(examId) is not { } path)
        {
            return ToText(await buildLines(examId, ct));
        }

        var gate = _gates.GetOrAdd(examId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (File.Exists(path))
            {
                return await File.ReadAllTextAsync(path, Utf8, ct);
            }

            var text = ToText(await buildLines(examId, ct));
            try
            {
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, text, Utf8, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Dosyaya yazılamasa da içerik gösterilir.
                logger.LogError(ex, "Sınav log dosyası yeniden üretilemedi: {Path}", path);
            }

            return text;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string ToText(IReadOnlyList<string> lines) =>
        lines.Count == 0 ? "" : string.Join(Environment.NewLine, lines) + Environment.NewLine;

    public async Task WriteAsync(int examId, string line, CancellationToken ct = default)
    {
        if (GetPath(examId) is not { } path)
        {
            return;
        }

        var gate = _gates.GetOrAdd(examId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.AppendAllTextAsync(path, line + Environment.NewLine, Utf8, ct);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Sınav log dosyasına yazılamadı: {Path}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Sınav log dosyasına yazılamadı: {Path}", path);
        }
        finally
        {
            gate.Release();
        }
    }
}
