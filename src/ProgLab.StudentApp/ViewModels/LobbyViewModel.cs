using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.StudentApp.ViewModels;

/// <summary>Lobideki sınav kartı. Saatler bilgisayarın yerel saatiyle gösterilir, süre sunucudan gelir.</summary>
public sealed record LobbyExamRow(StudentExamSummaryDto Exam)
{
    public string Title => Exam.Title;

    public string KindText => Exam.Kind == ExamKind.Exam ? "Sınav" : "Ödev";

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
public sealed partial class LobbyViewModel(ApiClient api, ExamHubClient? hub, string fullName, Action<Action> ui) : ObservableObject
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
        }

        _ = RefreshLoopAsync(cts.Token);
    }

    public void Stop()
    {
        _running?.Cancel();
        _running = null;

        if (hub is not null)
        {
            hub.ExamPublished -= OnExamChanged;
            hub.ExamEnded -= OnExamChanged;
            hub.StateChanged -= OnHubStateChanged;
            _ = hub.StopAsync();
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
                Exams.Add(new LobbyExamRow(exam));
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
