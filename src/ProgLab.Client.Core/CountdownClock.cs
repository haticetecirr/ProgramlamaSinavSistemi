using System.Diagnostics;

namespace ProgLab.Client.Core;

/// <summary>
/// Sınav sayacı. Yalnızca sunucudan gelen kalan süreyi ve geçen süreyi kullanır; bilgisayarın saatine hiç bakmaz.
/// Stopwatch işletim sisteminin monoton sayacıdır, bu yüzden saati ileri almak sayacı etkilemez.
/// </summary>
public sealed class CountdownClock
{
    private readonly Func<TimeSpan> _elapsed;
    private TimeSpan _syncedAt;
    private int _syncedRemaining;

    public CountdownClock()
        : this(StartStopwatch())
    {
    }

    /// <param name="elapsed">Testlerde zamanı elle ilerletmek için.</param>
    public CountdownClock(Func<TimeSpan> elapsed)
    {
        _elapsed = elapsed;
    }

    public int RemainingSeconds =>
        Math.Max(0, (int)Math.Ceiling(_syncedRemaining - (_elapsed() - _syncedAt).TotalSeconds));

    /// <summary>Sunucunun bildirdiği kalan süreyle düzeltir.</summary>
    public void Sync(int remainingSeconds)
    {
        _syncedRemaining = remainingSeconds;
        _syncedAt = _elapsed();
    }

    /// <summary>Ekrandaki biçim: 39:59 veya 1 saatten uzunsa 1:05:09.</summary>
    public static string Format(int seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes:D2}:{time.Seconds:D2}";
    }

    private static Func<TimeSpan> StartStopwatch()
    {
        var stopwatch = Stopwatch.StartNew();
        return () => stopwatch.Elapsed;
    }
}
