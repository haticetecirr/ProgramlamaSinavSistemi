using System.Diagnostics;
using ProgLab.Shared.Dtos;

namespace ProgLab.Client.Core;

public enum AnswerSaveState
{
    Saved,
    Saving,
    Offline
}

/// <summary>
/// Cevapları kaybetmeden sunucuya taşır:
/// değişiklik kısa süre bekletilir (yazarken her harfte istek gitmesin), her soru için yalnızca son cevap gönderilir,
/// ağ hatasında cevap kuyrukta kalır ve bağlantı gelene kadar yeniden denenir.
/// Sunucunun reddettiği cevap (ör. süre doldu) yeniden denenmez.
/// </summary>
public sealed class AnswerSaveQueue : IAsyncDisposable
{
    private readonly Func<int, SaveAnswerRequest, CancellationToken, Task> _send;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _retryDelay;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<int, Entry> _pending = [];
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _lock = new();
    private readonly Task _worker;

    private long _version;
    private bool _isOffline;
    private TaskCompletionSource<bool>? _flush;

    public AnswerSaveQueue(Func<int, SaveAnswerRequest, CancellationToken, Task> send, TimeSpan? debounce = null, TimeSpan? retryDelay = null)
    {
        _send = send;
        _debounce = debounce ?? TimeSpan.FromSeconds(1);
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(3);
        _worker = Task.Run(RunAsync);
    }

    /// <summary>Kayıt durumu değişti.</summary>
    public event Action<AnswerSaveState>? StateChanged;

    /// <summary>Sunucu cevabı kesin olarak reddetti (ör. süre doldu). Bu cevap yeniden denenmez.</summary>
    public event Action<ApiException>? Rejected;

    public AnswerSaveState State { get; private set; } = AnswerSaveState.Saved;

    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _pending.Count;
            }
        }
    }

    public void Enqueue(int examQuestionId, SaveAnswerRequest answer)
    {
        lock (_lock)
        {
            _pending[examQuestionId] = new Entry(answer, ++_version, _clock.Elapsed + _debounce);
        }

        UpdateState();
        Wake();
    }

    /// <summary>Bekleyen her şeyi hemen gönderir. Hepsi gittiyse true; bağlantı yoksa false (cevaplar kuyrukta kalır).</summary>
    public Task<bool> FlushAsync()
    {
        TaskCompletionSource<bool> flush;
        lock (_lock)
        {
            if (_pending.Count == 0)
            {
                return Task.FromResult(true);
            }

            foreach (var id in _pending.Keys.ToList())
            {
                _pending[id] = _pending[id] with { DueAt = _clock.Elapsed };
            }

            flush = _flush ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        Wake();
        return flush.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        Wake();
        await _worker;
        _stop.Dispose();
        _wake.Dispose();
    }

    private async Task RunAsync()
    {
        var ct = _stop.Token;
        while (!ct.IsCancellationRequested)
        {
            int id;
            Entry entry;
            TimeSpan wait;
            lock (_lock)
            {
                if (_pending.Count == 0)
                {
                    id = 0;
                    entry = default;
                    wait = Timeout.InfiniteTimeSpan;
                }
                else
                {
                    var next = _pending.MinBy(p => p.Value.DueAt);
                    id = next.Key;
                    entry = next.Value;
                    wait = entry.DueAt - _clock.Elapsed;
                }
            }

            if (wait != Timeout.InfiniteTimeSpan && wait <= TimeSpan.Zero)
            {
                await SendAsync(id, entry, ct);
                continue;
            }

            try
            {
                // Yeni cevap gelirse hemen uyanır, yoksa ilk cevabın vakti gelince.
                await _wake.WaitAsync(wait, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task SendAsync(int id, Entry entry, CancellationToken ct)
    {
        try
        {
            await _send(id, entry.Answer, ct);
            OnSent(id, entry.Version);
        }
        catch (ApiException ex) when (ex.Code == ApiException.ConnectionFailed)
        {
            OnConnectionFailed();
        }
        catch (ApiException ex)
        {
            OnSent(id, entry.Version);
            Rejected?.Invoke(ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Kapanıyor.
        }
    }

    private void OnSent(int id, long version)
    {
        TaskCompletionSource<bool>? flush = null;
        lock (_lock)
        {
            // Gönderim sırasında yeni cevap geldiyse o kuyrukta kalır.
            if (_pending.TryGetValue(id, out var current) && current.Version == version)
            {
                _pending.Remove(id);
            }

            _isOffline = false;
            if (_pending.Count == 0)
            {
                flush = _flush;
                _flush = null;
            }
        }

        UpdateState();
        flush?.TrySetResult(true);
    }

    private void OnConnectionFailed()
    {
        TaskCompletionSource<bool>? flush;
        lock (_lock)
        {
            _isOffline = true;
            var retryAt = _clock.Elapsed + _retryDelay;
            foreach (var key in _pending.Keys.ToList())
            {
                _pending[key] = _pending[key] with { DueAt = retryAt };
            }

            flush = _flush;
            _flush = null;
        }

        UpdateState();
        flush?.TrySetResult(false);
    }

    /// <summary>Durum her seferinde kuyruğun o anki hâlinden hesaplanır; böylece iki iş parçacığı birbirini yanlış durumda bırakamaz.</summary>
    private void UpdateState()
    {
        AnswerSaveState state;
        lock (_lock)
        {
            state = _pending.Count == 0 ? AnswerSaveState.Saved
                : _isOffline ? AnswerSaveState.Offline
                : AnswerSaveState.Saving;
            if (state == State)
            {
                return;
            }

            State = state;
        }

        StateChanged?.Invoke(state);
    }

    private void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (ObjectDisposedException)
        {
            // Ekran kapanmış.
        }
    }

    private readonly record struct Entry(SaveAnswerRequest Answer, long Version, TimeSpan DueAt);
}
