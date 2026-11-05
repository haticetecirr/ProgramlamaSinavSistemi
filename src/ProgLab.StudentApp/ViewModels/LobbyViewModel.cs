using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.StudentApp.ViewModels;

/// <summary>Lobideki sınav kartı. Saatler bilgisayarın yerel saatiyle gösterilir, süre sunucudan gelir.</summary>
public sealed class LobbyExamRow
{
    public LobbyExamRow(StudentExamSummaryDto exam, Func<LobbyExamRow, Task>? join = null)
    {
        Exam = exam;
        JoinCommand = new AsyncRelayCommand(() => join?.Invoke(this) ?? Task.CompletedTask);
    }

    public StudentExamSummaryDto Exam { get; }

    public IAsyncRelayCommand JoinCommand { get; }

    public string Title => Exam.Title;

    public string KindText => Exam.Kind == ExamKind.Exam ? "Sınav" : "Ödev";

    public bool CanJoin => Exam.State == StudentExamState.Open;

    public string JoinText => Exam.HasStarted ? "Devam et" : "Katıl";

    /// <summary>Ekran okuyucu ve deneme betikleri için hangi sınava ait olduğunu söyler.</summary>
    public string JoinName => $"{JoinText}: {Title}";

    public string StateText => Exam.State switch
    {
        StudentExamState.Upcoming => "Yakında",
        StudentExamState.Open => "Açık",
        StudentExamState.Submitted => "Teslim edildi",
        _ => "Tamamlandı"
    };

    public string TimeText => Exam.State switch
    {
        StudentExamState.Upcoming => $"Başlangıç: {Exam.StartsAt.ToLocalTime():dd.MM.yyyy HH:mm}",
        StudentExamState.Open => $"Kalan süre: {Math.Ceiling(Exam.RemainingSeconds.GetValueOrDefault() / 60.0):0} dk · Bitiş: {Exam.EndsAt.ToLocalTime():HH:mm}",
        _ => $"Bitiş: {Exam.EndsAt.ToLocalTime():dd.MM.yyyy HH:mm}"
    };
}

/// <summary>
/// Öğrencinin sınav listesi. Her değişiklikte liste sunucudan yeniden çekilir: bildirim yalnızca "tazele" sinyalidir.
/// Ödevin başlangıç anı gibi bildirim gelmeyen durum değişiklikleri için liste belirli aralıkla da yenilenir.
/// </summary>
public sealed partial class LobbyViewModel(ApiClient api, ExamHubClient? hub, string fullName, Action<Action> ui, Action<int>? openPaper = null) : ObservableObject
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(20);

    private CancellationTokenSource? _running;
    private int _loadVersion;

    public ObservableCollection<LobbyExamRow> Exams { get; } = [];

    public string Greeting { get; } = $"Hoş geldiniz, {fullName}.";

    [ObservableProperty]
    private string _connectionText = "Bağlanıyor…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoaded;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsEmpty => IsLoaded && Exams.Count == 0;

    /// <summary>Bildirim gelince başlayan yenileme; testler bunun bitmesini bekleyebilir.</summary>
    public Task LastRefresh { get; private set; } = Task.CompletedTask;

    /// <summary>Ekran açıldığında: bildirimleri dinle ve aralıklı yenilemeyi başlat.</summary>
    public void Start()
    {
        Stop();
        var cts = _running = new CancellationTokenSource();

        if (hub is not null)
        {
            hub.ExamPublished += OnExamChanged;
            hub.ExamEnded += OnExamChanged;
            hub.StateChanged += OnHubStateChanged;
            hub.StartInBackground();
            if (hub.IsConnected)
            {
                ConnectionText = "Sunucuya bağlı"; // Sınav ekranından dönüldü: bağlantı zaten açık.
            }
        }

        _ = RefreshLoopAsync(cts.Token);
    }

    /// <summary>Ekrandan çıkınca dinlemeyi bırakır. Bağlantının kendisini ana pencere yönetir.</summary>
    public void Stop()
    {
        _running?.Cancel();
        _running = null;

        if (hub is not null)
        {
            hub.ExamPublished -= OnExamChanged;
            hub.ExamEnded -= OnExamChanged;
            hub.StateChanged -= OnHubStateChanged;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var version = ++_loadVersion;
        try
        {
            var exams = await api.GetLobbyAsync();
            if (version != _loadVersion)
            {
                return;
            }

            Exams.Clear();
            foreach (var exam in exams)
            {
                Exams.Add(new LobbyExamRow(exam, JoinAsync));
            }

            ErrorMessage = null;
            IsLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task JoinAsync(LobbyExamRow row)
    {
        ErrorMessage = null;
        try
        {
            await api.JoinExamAsync(row.Exam.Id);
            openPaper?.Invoke(row.Exam.Id);
        }
        catch (ApiException ex)
        {
            // Ör. sınav bu arada bitti: mesajı göster ve listeyi tazele.
            await RefreshAsync();
            ErrorMessage = ex.Message;
        }
    }

    private void OnExamChanged(ExamPublishedEvent _) => RequestRefresh();

    private void OnExamChanged(ExamEndedEvent _) => RequestRefresh();

    private void RequestRefresh() => ui(() => LastRefresh = RefreshCommand.ExecuteAsync(null));

    private void OnHubStateChanged() => ui(() =>
    {
        var connected = hub!.IsConnected;
        ConnectionText = connected ? "Sunucuya bağlı" : "Bağlantı yok — yeniden bağlanılıyor…";
        if (connected)
        {
            // Kopukken yayınlanan sınavlar kaçırılmış olabilir.
            LastRefresh = RefreshCommand.ExecuteAsync(null);
        }
    });

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        await RefreshCommand.ExecuteAsync(null);

        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await RefreshCommand.ExecuteAsync(null);
            }
        }
        catch (OperationCanceledException)
        {
            // Ekrandan çıkıldı.
        }
    }
}
