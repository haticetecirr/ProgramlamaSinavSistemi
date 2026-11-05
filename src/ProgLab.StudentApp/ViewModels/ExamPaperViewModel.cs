using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.StudentApp.ViewModels;

/// <summary>Teslim sonrası ekranda gösterilecek bilgi.</summary>
public sealed record ExamFinishedInfo(string Title, string Headline, string? Detail);

/// <summary>
/// Sınav çözme ekranı: soruları serbest gezme, kalan süre, otomatik kayıt ve teslim.
/// Süre yalnızca sunucudan gelir; bilgisayar saatine bakılmaz.
/// </summary>
public sealed partial class ExamPaperViewModel : ObservableObject
{
    private const int UrgentSeconds = 5 * 60;

    /// <summary>Süre bitmek üzereyken bekletme yapılmaz: sunucu bitişten sonra kabul etmez.</summary>
    private const int FlushBeforeEndSeconds = 5;

    private readonly ApiClient _api;
    private readonly ExamHubClient? _hub;
    private readonly int _examId;
    private readonly CountdownClock _clock;
    private readonly Action<Action> _ui;
    private readonly Action _onBack;
    private readonly Action<ExamFinishedInfo>? _onFinished;
    private readonly TimeSpan? _saveDebounce;
    private readonly TimeSpan? _saveRetryDelay;

    private CancellationTokenSource? _ticking;
    private AnswerSaveQueue? _queue;
    private long _submissionId;
    private ExamKind _kind;
    private bool _leaveWarned;
    private bool _finishing;
    private bool _hubWasDown;

    public ExamPaperViewModel(
        ApiClient api,
        ExamHubClient? hub,
        int examId,
        Action<Action> ui,
        Action onBack,
        CountdownClock? clock = null,
        Action<ExamFinishedInfo>? onFinished = null,
        TimeSpan? saveDebounce = null,
        TimeSpan? saveRetryDelay = null)
    {
        _api = api;
        _hub = hub;
        _examId = examId;
        _ui = ui;
        _onBack = onBack;
        _clock = clock ?? new CountdownClock();
        _onFinished = onFinished;
        _saveDebounce = saveDebounce;
        _saveRetryDelay = saveRetryDelay;
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

    /// <summary>Kaydedildi / Kaydediliyor / Bağlantı yok.</summary>
    [ObservableProperty]
    private string _saveStatusText = "Tüm cevaplar kaydedildi";

    [ObservableProperty]
    private bool _isSaveOffline;

    [ObservableProperty]
    private int _answeredCount;

    [ObservableProperty]
    private bool _isConfirmingSubmit;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsEditable => !IsClosed;

    public string PositionText => CurrentQuestion is null
        ? ""
        : $"{CurrentQuestion.Number} / {Questions.Count} · {AnsweredCount} cevaplandı";

    public string SubmitWarning
    {
        get
        {
            var empty = Questions.Count - AnsweredCount;
            var warning = empty > 0 ? $"{empty} soru boş. " : "";
            return _kind == ExamKind.Exam
                ? $"{warning}Teslim edince cevapları değiştiremezsiniz. Teslim edilsin mi?"
                : $"{warning}Teslim edilsin mi? Bitişe kadar yeniden teslim edebilirsiniz.";
        }
    }

    partial void OnAnsweredCountChanged(int value)
    {
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(SubmitWarning));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var paper = await _api.GetPaperAsync(_examId);

            Title = paper.Title;
            Description = paper.Description;
            _submissionId = paper.SubmissionId;
            _kind = paper.Kind;

            Questions.Clear();
            var number = 0;
            foreach (var question in paper.Questions)
            {
                var saved = paper.Answers.FirstOrDefault(a => a.ExamQuestionId == question.ExamQuestionId);
                Questions.Add(PaperQuestionViewModel.Create(question, ++number, saved, OnAnswerChanged));
            }

            AnsweredCount = Questions.Count(q => q.IsAnswered);
            CurrentQuestion = Questions.FirstOrDefault();

            var queue = new AnswerSaveQueue(
                (id, answer, ct) => _api.SaveAnswerAsync(_submissionId, id, answer, ct),
                _saveDebounce,
                _saveRetryDelay);
            queue.StateChanged += state => _ui(() => ShowSaveState(state));
            queue.Rejected += ex => _ui(() => OnSaveRejected(ex));
            _queue = queue;

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
    private void AskSubmit()
    {
        ErrorMessage = null;
        OnPropertyChanged(nameof(SubmitWarning));
        IsConfirmingSubmit = true;
    }

    [RelayCommand]
    private void CancelSubmit() => IsConfirmingSubmit = false;

    [RelayCommand]
    private async Task ConfirmSubmitAsync()
    {
        IsConfirmingSubmit = false;
        ErrorMessage = null;

        // Önce bekleyen cevaplar gitmeli: aksi hâlde eksik cevaplarla teslim edilirdi.
        if (_queue is not null && !await _queue.FlushAsync())
        {
            ErrorMessage = "Bağlantı yok, teslim edilemedi. Cevaplarınız bağlantı gelince gönderilecek; sonra yeniden deneyin.";
            return;
        }

        try
        {
            var result = await _api.SubmitAsync(_submissionId);
            Finish(new ExamFinishedInfo(
                Title,
                $"Teslim alındı: {result.SubmittedAt.ToLocalTime():dd.MM.yyyy HH:mm:ss}",
                _kind == ExamKind.Assignment
                    ? $"{result.AnsweredCount}/{Questions.Count} soru cevaplandı. Bitişe kadar yeniden teslim edebilirsiniz."
                    : $"{result.AnsweredCount}/{Questions.Count} soru cevaplandı."));
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task BackToLobbyAsync()
    {
        if (_queue is not null && !await _queue.FlushAsync() && !_leaveWarned)
        {
            _leaveWarned = true;
            ErrorMessage = "Bağlantı yok: gönderilemeyen cevaplar var. Çıkarsanız kaybolur; yine de çıkmak için tekrar basın.";
            return;
        }

        _onBack();
    }

    /// <summary>Ekran açıldığında: saniyelik sayaç ve bildirimler.</summary>
    public void Start()
    {
        StopListening();
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

    /// <summary>Ekrandan çıkarken: dinlemeyi bırak, kuyruğu kapat.</summary>
    public void Stop()
    {
        StopListening();
        var queue = _queue;
        _queue = null;
        if (queue is not null)
        {
            _ = queue.DisposeAsync().AsTask();
        }
    }

    /// <summary>Sayacı ekrana yansıtır; süre bittiyse ekranı kapatır. Saniyede bir çağrılır.</summary>
    public void Tick()
    {
        var remaining = _clock.RemainingSeconds;
        TimeText = CountdownClock.Format(remaining);
        IsUrgent = remaining <= UrgentSeconds;

        if (remaining <= FlushBeforeEndSeconds && !IsClosed && _queue is not null)
        {
            _ = _queue.FlushAsync();
        }

        if (remaining <= 0 && !IsClosed)
        {
            Close("Süre doldu. Bu sınavda artık değişiklik yapılamaz.");
            _ = TrySubmitAtTimeUpAsync();
        }
    }

    private void StopListening()
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

    private void OnAnswerChanged(PaperQuestionViewModel question)
    {
        AnsweredCount = Questions.Count(q => q.IsAnswered);
        if (!IsClosed)
        {
            _queue?.Enqueue(question.ExamQuestionId, question.ToSaveRequest());
        }
    }

    private void ShowSaveState(AnswerSaveState state)
    {
        IsSaveOffline = state == AnswerSaveState.Offline;
        SaveStatusText = state switch
        {
            AnswerSaveState.Saved => "Tüm cevaplar kaydedildi",
            AnswerSaveState.Saving => "Kaydediliyor…",
            _ => "Bağlantı yok — cevaplarınız bağlantı gelince gönderilecek"
        };

        if (state != AnswerSaveState.Offline)
        {
            _leaveWarned = false;
        }
    }

    private void OnSaveRejected(ApiException ex)
    {
        // Süre sunucuda bitti: sayaç geride kalsa bile ekran kapanır.
        if (ex.Code == ProgLab.Shared.ErrorCodes.ExamClosed)
        {
            _clock.Sync(0);
            Tick();
        }
        else
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// Sayaç bitince son bir kez teslim denenir. Başarısız olması sorun değildir:
    /// sunucu süre sonunda kayıtlı cevapları zaten otomatik teslim eder.
    /// </summary>
    private async Task TrySubmitAtTimeUpAsync()
    {
        try
        {
            var result = await _api.SubmitAsync(_submissionId);
            Finish(new ExamFinishedInfo(Title, $"Süre doldu. Teslim alındı: {result.SubmittedAt.ToLocalTime():dd.MM.yyyy HH:mm:ss}", $"{result.AnsweredCount}/{Questions.Count} soru cevaplandı."));
        }
        catch (ApiException)
        {
            Finish(new ExamFinishedInfo(Title, "Süre doldu.", "Sunucuya kaydedilen cevaplarınız otomatik olarak teslim edildi."));
        }
    }

    private void Finish(ExamFinishedInfo info)
    {
        if (_finishing)
        {
            return;
        }

        _finishing = true;
        _onFinished?.Invoke(info);
    }

    private void Close(string message)
    {
        IsClosed = true;
        IsConfirmingSubmit = false;
        ClosedMessage = message;
    }

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
        if (e.ExamId != _examId)
        {
            return;
        }

        _ui(() =>
        {
            var manual = e.Reason == "Manual";
            _clock.Sync(0);
            TimeText = CountdownClock.Format(0);
            Close(manual ? "Sınav eğitmen tarafından bitirildi. Bu sınavda artık değişiklik yapılamaz." : "Süre doldu. Bu sınavda artık değişiklik yapılamaz.");
            Finish(new ExamFinishedInfo(
                Title,
                manual ? "Sınav eğitmen tarafından bitirildi." : "Süre doldu.",
                "Sunucuya kaydedilen cevaplarınız otomatik olarak teslim edildi."));
        });
    }

    private void OnHubStateChanged() => _ui(() =>
    {
        var connected = _hub!.IsConnected;
        ConnectionText = connected ? "Sunucuya bağlı" : "Bağlantı yok — yeniden bağlanılıyor…";

        if (!connected)
        {
            _hubWasDown = true;
        }
        else if (_hubWasDown)
        {
            _hubWasDown = false;
            _ = ResyncAsync();
        }
    });

    /// <summary>Kopukken gelen bitiş bildirimi kaçmış olabilir: bağlantı dönünce kalan süre sunucudan yeniden alınır.</summary>
    private async Task ResyncAsync()
    {
        try
        {
            var paper = await _api.GetPaperAsync(_examId);
            _ui(() =>
            {
                _clock.Sync(paper.RemainingSeconds);
                Tick();
            });
        }
        catch (ApiException ex) when (ex.Code == ProgLab.Shared.ErrorCodes.ExamClosed)
        {
            _ui(() =>
            {
                _clock.Sync(0);
                Tick();
            });
        }
        catch (ApiException)
        {
            // Bağlantı yine koptu; sayaç sürüyor, sonraki bağlantıda yeniden denenir.
        }
    }

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
