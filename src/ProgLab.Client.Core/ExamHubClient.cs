using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;

namespace ProgLab.Client.Core;

/// <summary>Sunucudan gelen gerçek zamanlı bildirimleri dinler. Hub'a komut gönderilmez; komutlar REST ile gider.</summary>
public sealed class ExamHubClient : IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(10);

    private readonly HubConnection _connection;
    private CancellationTokenSource? _startLoop;
    private TaskCompletionSource _ready = NewReadySignal();
    private volatile bool _isReady;

    public ExamHubClient(Uri baseAddress, TokenStore tokens, Action<HttpConnectionOptions>? configure = null)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(baseAddress, HubEvents.HubPath.TrimStart('/')), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(tokens.Token);
                configure?.Invoke(options);
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        _connection.On<ExamPublishedEvent>(HubEvents.ExamPublished, e => ExamPublished?.Invoke(e));
        _connection.On<ExamEndedEvent>(HubEvents.ExamEnded, e => ExamEnded?.Invoke(e));
        _connection.On<StudentConnectionDto>(HubEvents.StudentConnected, e => StudentConnected?.Invoke(e));
        _connection.On<StudentConnectionDto>(HubEvents.StudentDisconnected, e => StudentDisconnected?.Invoke(e));
        _connection.On<TimeSyncEvent>(HubEvents.TimeSync, e => TimeSync?.Invoke(e));
        _connection.On(HubEvents.Ready, OnReady);

        _connection.Reconnecting += _ => OnConnectionLost(null);
        _connection.Closed += OnConnectionLost;
    }

    public event Action<ExamPublishedEvent>? ExamPublished;

    public event Action<ExamEndedEvent>? ExamEnded;

    public event Action<TimeSyncEvent>? TimeSync;

    public event Action<StudentConnectionDto>? StudentConnected;

    public event Action<StudentConnectionDto>? StudentDisconnected;

    /// <summary>Bağlantı durumu değişti. Bağlanınca kaçırılan olaylar için liste tazelenmelidir.</summary>
    public event Action? StateChanged;

    /// <summary>Bağlantı açık ve sunucu gruplara eklemeyi bitirdi: bildirimler artık ulaşır.</summary>
    public bool IsConnected => _isReady && _connection.State == HubConnectionState.Connected;

    /// <summary>Bir kez bağlanmayı dener; bildirim almaya hazır olunca döner, başarısızsa hata fırlatır.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        var ready = _ready = NewReadySignal();
        await _connection.StartAsync(ct);
        await ready.Task.WaitAsync(ReadyTimeout, ct);
    }

    /// <summary>Arka planda bağlanana kadar dener; sunucu sonradan açılsa da bağlantı kurulur.</summary>
    public void StartInBackground()
    {
        // Bağlıysa veya zaten bağlanmaya çalışıyorsa yeniden başlatılmaz (lobi ile sınav ekranı aynı bağlantıyı kullanır).
        if (_connection.State != HubConnectionState.Disconnected || _startLoop is { IsCancellationRequested: false })
        {
            return;
        }

        var cts = _startLoop = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested && _connection.State == HubConnectionState.Disconnected)
            {
                try
                {
                    await StartAsync(cts.Token);
                    cts.Cancel(); // Döngü bitti; sonraki StartInBackground yeniden başlayabilsin.
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cts.IsCancellationRequested)
                {
                    StateChanged?.Invoke();
                    await Task.Delay(RetryDelay, CancellationToken.None);
                }
            }
        }, CancellationToken.None);
    }

    public async Task StopAsync()
    {
        _startLoop?.Cancel();
        await _connection.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _startLoop?.Cancel();
        await _connection.DisposeAsync();
    }

    private static TaskCompletionSource NewReadySignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void OnReady()
    {
        _isReady = true;
        _ready.TrySetResult();
        StateChanged?.Invoke();
    }

    private Task OnConnectionLost(Exception? error)
    {
        _isReady = false;
        if (_connection.State == HubConnectionState.Disconnected)
        {
            // Sunucu bağlantıyı hazır olmadan kapattıysa (ör. pasif hesap) bekleyen StartAsync hemen hata alsın.
            _ready.TrySetException(error ?? new InvalidOperationException("Bağlantı kapandı."));
        }

        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            retryContext.PreviousRetryCount == 0 ? TimeSpan.Zero : RetryDelay;
    }
}
