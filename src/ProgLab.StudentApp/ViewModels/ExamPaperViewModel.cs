using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;

namespace ProgLab.StudentApp.ViewModels;

/// <summary>
/// Sınav çözme ekranı: soruları serbest gezme, kalan süre, sunucuyla süre düzeltmesi.
/// Cevapların sunucuya kaydı ve teslim Aşama 8'de eklenir.
/// </summary>
public sealed partial class ExamPaperViewModel : ObservableObject
{
    private const int UrgentSeconds = 5 * 60;

    private readonly ApiClient _api;
    private readonly ExamHubClient? _hub;
    private readonly int _examId;
    private readonly CountdownClock _clock;
    private readonly Action<Action> _ui;
    private readonly Action _onBack;
    private CancellationTokenSource? _ticking;

    public ExamPaperViewModel(ApiClient api, ExamHubClient? hub, int examId, Action<Action> ui, Action onBack, CountdownClock? clock = null)
    {
        _api = api;
        _hub = hub;
        _examId = examId;
        _ui = ui;
        _onBack = onBack;
        _clock = clock ?? new CountdownClock();
    }

    public ObservableCollection<PaperQuestionViewModel> Questions { get; } = [];

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private PaperQuestionViewModel? _currentQuestion;

    [ObservableProperty]
    private string _timeText = "--:--";

    /// <summary>Son 5 dakikada sayaç kırmızıya döner.</summary>
    [ObservableProperty]
    private bool _isUrgent;

    /// <summary>Süre dolduysa veya sınav bittiyse ekran salt okunur olur.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    private bool _isClosed;

    [ObservableProperty]
    private string? _closedMessage;

    [ObservableProperty]
    private string _connectionText = "";

    [ObservableProperty]
    private int _answeredCount;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsEditable => !IsClosed;

    public string PositionText => CurrentQuestion is null
        ? ""
        : $"{CurrentQuestion.Number} / {Questions.Count} · {AnsweredCount} cevaplandı";

    /// <summary>Aşama 8'de kayıt durumu (Kaydedildi / Kaydediliyor / Bağlantı yok) buraya gelir.</summary>
    public string SaveStatusText => "Otomatik kayıt henüz etkin değil: cevaplar sunucuya gönderilmiyor.";

    partial void OnAnsweredCountChanged(int value) => OnPropertyChanged(nameof(PositionText));

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var paper = await _api.GetPaperAsync(_examId);

            Title = paper.Title;
            Description = paper.Description;

            Questions.Clear();
            var number = 0;
            foreach (var question in paper.Questions)
            {
                var saved = paper.Answers.FirstOrDefault(a => a.ExamQuestionId == question.ExamQuestionId);
                Questions.Add(PaperQuestionViewModel.Create(question, ++number, saved, UpdateAnsweredCount));
            }

            UpdateAnsweredCount();
            CurrentQuestion = Questions.FirstOrDefault();

            // Süre yalnızca sunucudan gelir.
            _clock.Sync(paper.RemainingSeconds);
            Tick();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Previous()
    {
        var index = CurrentQuestion is null ? -1 : Questions.IndexOf(CurrentQuestion);
        if (index > 0)
        {
            CurrentQuestion = Questions[index - 1];
        }
    }

    [RelayCommand]
    private void Next()
    {
        var index = CurrentQuestion is null ? -1 : Questions.IndexOf(CurrentQuestion);
        if (index >= 0 && index < Questions.Count - 1)
        {
            CurrentQuestion = Questions[index + 1];
        }
    }

    [RelayCommand]
    private void BackToLobby() => _onBack();

    /// <summary>Ekran açıldığında: saniyelik sayaç ve bildirimler.</summary>
    public void Start()
    {
        Stop();
        var cts = _ticking = new CancellationTokenSource();

        if (_hub is not null)
        {
            _hub.TimeSync += OnTimeSync;
            _hub.ExamEnded += OnExamEnded;
            _hub.StateChanged += OnHubStateChanged;
            OnHubStateChanged();
        }

        _ = TickLoopAsync(cts.Token);
    }

    public void Stop()
    {
        _ticking?.Cancel();
        _ticking = null;

        if (_hub is not null)
        {
            _hub.TimeSync -= OnTimeSync;
            _hub.ExamEnded -= OnExamEnded;
            _hub.StateChanged -= OnHubStateChanged;
        }
    }

    /// <summary>Sayacı ekrana yansıtır; süre bittiyse ekranı kapatır. Saniyede bir çağrılır.</summary>
    public void Tick()
    {
        var remaining = _clock.RemainingSeconds;
        TimeText = CountdownClock.Format(remaining);
        IsUrgent = remaining <= UrgentSeconds;

        if (remaining <= 0)
        {
            Close("Süre doldu. Bu sınavda artık değişiklik yapılamaz.");
        }
    }

    private void Close(string message)
    {
        IsClosed = true;
        ClosedMessage = message;
    }

    private void UpdateAnsweredCount() => AnsweredCount = Questions.Count(q => q.IsAnswered);

    private void OnTimeSync(TimeSyncEvent e)
    {
        if (e.ExamId == _examId)
        {
            _ui(() =>
            {
                _clock.Sync(e.RemainingSeconds);
                Tick();
            });
        }
    }

    private void OnExamEnded(ExamEndedEvent e)
    {
        if (e.ExamId == _examId)
        {
            _ui(() =>
            {
                _clock.Sync(0);
                Tick();
                Close("Sınav eğitmen tarafından bitirildi. Bu sınavda artık değişiklik yapılamaz.");
            });
        }
    }

    private void OnHubStateChanged() => _ui(() =>
        ConnectionText = _hub!.IsConnected ? "Sunucuya bağlı" : "Bağlantı yok — yeniden bağlanılıyor…");

    private async Task TickLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                _ui(Tick);
            }
        }
        catch (OperationCanceledException)
        {
            // Ekrandan çıkıldı.
        }
    }
}
