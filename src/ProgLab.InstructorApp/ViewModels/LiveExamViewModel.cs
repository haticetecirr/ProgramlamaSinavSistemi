using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Canlı izleme tablosundaki öğrenci satırı.</summary>
public sealed partial class LiveStudentRow(LiveStudentDto student, int totalQuestions) : ObservableObject
{
    public int StudentId { get; } = student.StudentId;

    public string Username { get; } = student.Username;

    public string FullName { get; } = student.FullName;

    public string? Ip { get; } = student.Ip;

    public bool IsInProgress { get; } = student.Status == SubmissionStatus.InProgress;

    public string ConnectionText => student.IsConnected ? "Bağlı" : "Bağlı değil";

    public string StatusText => student.Status switch
    {
        SubmissionStatus.Submitted => $"Teslim etti ({student.SubmittedAt?.ToLocalTime():HH:mm:ss})",
        SubmissionStatus.AutoSubmitted => $"Otomatik teslim ({student.SubmittedAt?.ToLocalTime():HH:mm:ss})",
        SubmissionStatus.InProgress => "Çözüyor",
        _ => "Katılmadı"
    };

    public string ProgressText => $"{AnsweredCount}/{totalQuestions}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _answeredCount = student.AnsweredCount;
}

/// <summary>Sınav sürerken eğitmen ekranı: kalan süre, öğrenci tablosu, uyarılar, bitirme.</summary>
public sealed partial class LiveExamViewModel(ApiClient api, ExamHubClient? hub, int examId, Action<Action> ui, Action onBack, Action<int>? openResults = null, CountdownClock? clock = null) : ObservableObject
{
    private const int UrgentSeconds = 5 * 60;

    private readonly CountdownClock _clock = clock ?? new CountdownClock();
    private CancellationTokenSource? _ticking;
    private int _loadVersion;

    public ObservableCollection<LiveStudentRow> Students { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _timeText = "--:--";

    [ObservableProperty]
    private bool _isUrgent;

    [ObservableProperty]
    private string _summaryText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private ExamStatus _status = ExamStatus.Published;

    [ObservableProperty]
    private bool _isConfirmingEnd;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsRunning => Status == ExamStatus.Published;

    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Bildirim sonrası yenileme; testler bunun bitmesini bekleyebilir.</summary>
    public Task LastRefresh { get; private set; } = Task.CompletedTask;

    public void Start()
    {
        Stop();
        var cts = _ticking = new CancellationTokenSource();
        if (hub is not null)
        {
            hub.AnswerProgress += OnAnswerProgress;
            hub.SubmissionReceived += OnSubmissionReceived;
            hub.SecondDeviceWarning += OnSecondDevice;
            hub.StudentConnected += OnStudentConnectionChanged;
            hub.StudentDisconnected += OnStudentConnectionChanged;
            hub.ExamEnded += OnExamEnded;
            hub.StateChanged += OnHubStateChanged;
        }

        _ = TickLoopAsync(cts.Token);
    }

    public void Stop()
    {
        _ticking?.Cancel();
        _ticking = null;
        if (hub is not null)
        {
            hub.AnswerProgress -= OnAnswerProgress;
            hub.SubmissionReceived -= OnSubmissionReceived;
            hub.SecondDeviceWarning -= OnSecondDevice;
            hub.StudentConnected -= OnStudentConnectionChanged;
            hub.StudentDisconnected -= OnStudentConnectionChanged;
            hub.ExamEnded -= OnExamEnded;
            hub.StateChanged -= OnHubStateChanged;
        }
    }

    public void Tick()
    {
        var remaining = _clock.RemainingSeconds;
        TimeText = CountdownClock.Format(remaining);
        IsUrgent = remaining <= UrgentSeconds;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        try
        {
            var live = await api.GetLiveExamAsync(examId);
            if (version != _loadVersion)
            {
                return;
            }

            Title = live.Title;
            Status = live.Status;
            _clock.Sync(live.RemainingSeconds);
            Tick();

            Students.Clear();
            foreach (var student in live.Students)
            {
                Students.Add(new LiveStudentRow(student, live.TotalQuestions));
            }

            Warnings.Clear();
            foreach (var warning in live.Warnings)
            {
                Warnings.Add(warning);
            }

            OnPropertyChanged(nameof(HasWarnings));

            var connected = live.Students.Count(s => s.IsConnected);
            var submitted = live.Students.Count(s => s.Status is SubmissionStatus.Submitted or SubmissionStatus.AutoSubmitted);
            SummaryText = $"{live.Students.Count} öğrenci · {connected} bağlı · {submitted} teslim";
            ErrorMessage = null;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void AskEnd() => IsConfirmingEnd = true;

    [RelayCommand]
    private void CancelEnd() => IsConfirmingEnd = false;

    [RelayCommand]
    private async Task ConfirmEndAsync()
    {
        IsConfirmingEnd = false;
        try
        {
            await api.EndExamAsync(examId);
            await LoadCommand.ExecuteAsync(null);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Back() => onBack();

    [RelayCommand]
    private void OpenResults() => openResults?.Invoke(examId);

    private void RequestRefresh() => ui(() => LastRefresh = LoadCommand.ExecuteAsync(null));

    private void OnAnswerProgress(AnswerProgressEvent e)
    {
        if (e.ExamId == examId)
        {
            ui(() =>
            {
                // Sık gelir: tüm liste yerine yalnızca satır güncellenir.
                // Durum da değiştiyse (yeni katıldı, teslim edilen ödev düzenlendi) liste tazelenir.
                if (Students.FirstOrDefault(s => s.StudentId == e.StudentId) is { IsInProgress: true } row)
                {
                    row.AnsweredCount = e.AnsweredCount;
                }
                else
                {
                    LastRefresh = LoadCommand.ExecuteAsync(null);
                }
            });
        }
    }

    private void OnSubmissionReceived(SubmissionReceivedEvent e)
    {
        if (e.ExamId == examId)
        {
            RequestRefresh();
        }
    }

    private void OnSecondDevice(SecondDeviceEvent e)
    {
        if (e.ExamId == examId)
        {
            RequestRefresh();
        }
    }

    private void OnStudentConnectionChanged(StudentConnectionDto _) => RequestRefresh();

    private void OnExamEnded(ExamEndedEvent e)
    {
        if (e.ExamId == examId)
        {
            RequestRefresh();
        }
    }

    /// <summary>Kopukken kaçan bildirimler olabilir: bağlantı dönünce tablo sunucudan yeniden alınır.</summary>
    private void OnHubStateChanged()
    {
        if (hub!.IsConnected)
        {
            RequestRefresh();
        }
    }

    private async Task TickLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                ui(Tick);
            }
        }
        catch (OperationCanceledException)
        {
            // Ekrandan çıkıldı.
        }
    }
}
