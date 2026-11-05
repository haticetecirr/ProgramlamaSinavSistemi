using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Sınava eklenmiş bir soru: sınava özel puanı ve sıralama düğmeleriyle.</summary>
public sealed partial class ExamQuestionRow(
    int questionId,
    string title,
    string details,
    decimal points,
    Action<ExamQuestionRow> moveUp,
    Action<ExamQuestionRow> moveDown,
    Action<ExamQuestionRow> remove,
    Action onPointsChanged) : ObservableObject
{
    public int QuestionId { get; } = questionId;

    public string Title { get; } = title;

    public string Details { get; } = details;

    [ObservableProperty]
    private decimal? _points = points;

    partial void OnPointsChanged(decimal? value) => onPointsChanged();

    [RelayCommand]
    private void MoveUp() => moveUp(this);

    [RelayCommand]
    private void MoveDown() => moveDown(this);

    [RelayCommand]
    private void Remove() => remove(this);
}

/// <summary>Sınav ve ödev hazırlama. Taslak değilse salt okunur açılır.</summary>
public sealed partial class ExamEditorViewModel : ObservableObject
{
    private const int DefaultDuration = 40;

    private readonly ApiClient _api;
    private readonly Action<string?> _onClosed;
    private int? _examId;
    private uint? _rowVersion;

    public ExamEditorViewModel(ApiClient api, int? examId, Action<string?> onClosed)
    {
        _api = api;
        _examId = examId;
        _onClosed = onClosed;
        Questions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TotalText));
    }

    public ObservableCollection<ClassDto> Classes { get; } = [];

    public ObservableCollection<QuestionRow> BankQuestions { get; } = [];

    public ObservableCollection<ExamQuestionRow> Questions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDraft))]
    private ExamStatus _status = ExamStatus.Draft;

    [ObservableProperty]
    private string _heading = "Yeni Sınav";

    [ObservableProperty]
    private string _examTitle = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAssignment), nameof(PublishText), nameof(PublishWarning))]
    private bool _isExam = true;

    [ObservableProperty]
    private ClassDto? _selectedClass;

    [ObservableProperty]
    private decimal? _durationMinutes = DefaultDuration;

    [ObservableProperty]
    private DateTime? _startDate;

    // Saat seçici boşken Avalonia İngilizce "hour/minute" yazar; alan hep dolu başlar.
    [ObservableProperty]
    private TimeSpan? _startTime = new(9, 0, 0);

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private TimeSpan? _endTime = new(23, 59, 0);

    [ObservableProperty]
    private string _bankSearch = "";

    [ObservableProperty]
    private QuestionRow? _selectedBankQuestion;

    [ObservableProperty]
    private bool _isConfirmingPublish;

    [ObservableProperty]
    private string? _scheduleText;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsDraft => Status == ExamStatus.Draft;

    public bool IsAssignment
    {
        get => !IsExam;
        set => IsExam = !value;
    }

    public string PublishText => IsExam ? "Yayınla ve başlat" : "Yayınla";

    public string PublishWarning => IsExam
        ? "Sınav şimdi başlayacak ve süresi işlemeye başlayacak. Yayınlandıktan sonra sınav ve soruları değiştirilemez. Devam edilsin mi?"
        : "Ödev şubeye dağıtılacak ve başlangıç saatinde kendiliğinden açılacak. Yayınlandıktan sonra ödev ve soruları değiştirilemez. Devam edilsin mi?";

    public string TotalText => $"{Questions.Count} soru · Toplam {Questions.Sum(q => q.Points ?? 0):0.##} puan";

    [RelayCommand]
    private async Task LoadAsync() => await RunAsync(async () =>
    {
        var classes = await _api.GetClassesAsync();
        Classes.Clear();
        foreach (var item in classes)
        {
            Classes.Add(item);
        }

        await LoadBankCoreAsync();

        if (_examId is { } id)
        {
            ApplyExam(await _api.GetExamAsync(id));
        }
        else
        {
            SelectedClass = Classes.FirstOrDefault();
        }
    });

    [RelayCommand]
    private Task SearchBankAsync() => RunAsync(LoadBankCoreAsync);

    [RelayCommand]
    private void AddSelectedQuestion()
    {
        ErrorMessage = null;
        if (SelectedBankQuestion is not { } row)
        {
            ErrorMessage = "Önce bankadan bir soru seçin.";
            return;
        }

        if (Questions.Any(q => q.QuestionId == row.Question.Id))
        {
            ErrorMessage = "Bu soru sınava zaten eklendi.";
            return;
        }

        Questions.Add(CreateRow(row.Question.Id, row.Title, row.Details, row.Question.DefaultPoints));
    }

    [RelayCommand]
    private Task SaveDraftAsync() => RunAsync(async () =>
    {
        if (await SaveCoreAsync())
        {
            _onClosed($"\"{ExamTitle.Trim()}\" taslak olarak kaydedildi.");
        }
    });

    [RelayCommand]
    private void AskPublish()
    {
        ErrorMessage = null;
        IsConfirmingPublish = true;
    }

    [RelayCommand]
    private void CancelPublish() => IsConfirmingPublish = false;

    [RelayCommand]
    private Task ConfirmPublishAsync() => RunAsync(async () =>
    {
        IsConfirmingPublish = false;
        if (!await SaveCoreAsync())
        {
            return;
        }

        var published = await _api.PublishExamAsync(_examId!.Value);
        _onClosed($"\"{published.Title}\" yayınlandı. Bitiş: {published.EndsAt!.Value.ToLocalTime():dd.MM.yyyy HH:mm}");
    });

    [RelayCommand]
    private void Close() => _onClosed(null);

    /// <summary>Formu kaydeder; form hatası varsa false döner.</summary>
    private async Task<bool> SaveCoreAsync()
    {
        if (string.IsNullOrWhiteSpace(ExamTitle))
        {
            ErrorMessage = "Başlık zorunludur.";
            return false;
        }

        if (Questions.Any(q => q.Points is not > 0))
        {
            ErrorMessage = "Her sorunun puanı 0'dan büyük olmalıdır.";
            return false;
        }

        var startsAt = ToLocalInstant(StartDate, StartTime);
        var endsAt = ToLocalInstant(EndDate, EndTime);
        if (!IsExam && (startsAt is null || endsAt is null))
        {
            ErrorMessage = "Ödev için başlangıç ve bitiş tarih-saatini girin.";
            return false;
        }

        var request = new SaveExamRequest(
            ExamTitle,
            Description,
            IsExam ? ExamKind.Exam : ExamKind.Assignment,
            SelectedClass?.Id,
            IsExam ? (int?)DurationMinutes : null,
            IsExam ? null : startsAt,
            IsExam ? null : endsAt,
            _rowVersion);

        var saved = _examId is { } id
            ? await _api.UpdateExamAsync(id, request)
            : await _api.CreateExamAsync(request);

        _examId = saved.Id;
        Heading = "Taslağı Düzenle";

        var list = Questions.Select((q, index) => new ExamQuestionInput(q.QuestionId, (short)(index + 1), q.Points)).ToList();
        var withQuestions = await _api.ReplaceExamQuestionsAsync(saved.Id, list);
        _rowVersion = withQuestions.RowVersion;
        return true;
    }

    private async Task LoadBankCoreAsync()
    {
        var result = await _api.GetQuestionsAsync(search: BankSearch, pageSize: 200);
        BankQuestions.Clear();
        foreach (var question in result.Items)
        {
            BankQuestions.Add(new QuestionRow(question));
        }
    }

    private void ApplyExam(InstructorExamDto exam)
    {
        _rowVersion = exam.RowVersion;
        Status = exam.Status;
        Heading = exam.Status switch
        {
            ExamStatus.Draft => "Taslağı Düzenle",
            ExamStatus.Published => "Yayındaki Sınav (salt okunur)",
            _ => "Tamamlanan Sınav (salt okunur)"
        };
        ExamTitle = exam.Title;
        Description = exam.Description ?? "";
        IsExam = exam.Kind == ExamKind.Exam;
        SelectedClass = Classes.FirstOrDefault(c => c.Id == exam.ClassId);
        DurationMinutes = exam.DurationMinutes;

        if (exam.StartsAt is { } startInstant)
        {
            (StartDate, StartTime) = ToLocalParts(startInstant);
        }

        if (exam.EndsAt is { } endInstant)
        {
            (EndDate, EndTime) = ToLocalParts(endInstant);
        }

        ScheduleText = exam.Status != ExamStatus.Draft && exam.StartsAt is { } start && exam.EndsAt is { } end
            ? $"Başlangıç: {start.ToLocalTime():dd.MM.yyyy HH:mm} · Bitiş: {end.ToLocalTime():dd.MM.yyyy HH:mm}"
            : null;

        Questions.Clear();
        foreach (var question in exam.Questions)
        {
            Questions.Add(CreateRow(question.QuestionId, question.Title, DescribeExamQuestion(question), question.Points));
        }

        OnPropertyChanged(nameof(TotalText));
    }

    private static string DescribeExamQuestion(ExamQuestionDto q)
    {
        var type = q.Type == QuestionType.MultipleChoice ? "Çoktan seçmeli" : "Klasik";
        var note = q.IsDeleted ? " · BANKADAN SİLİNMİŞ" : q.IsCurrent ? "" : " · Eski sürüm";
        return $"{type} · Sürüm {q.VersionNo}{note}";
    }

    private ExamQuestionRow CreateRow(int questionId, string title, string details, decimal points) =>
        new(questionId, title, details, points, MoveUp, MoveDown, row => Questions.Remove(row), () => OnPropertyChanged(nameof(TotalText)));

    private void MoveUp(ExamQuestionRow row)
    {
        var index = Questions.IndexOf(row);
        if (index > 0)
        {
            Questions.Move(index, index - 1);
        }
    }

    private void MoveDown(ExamQuestionRow row)
    {
        var index = Questions.IndexOf(row);
        if (index >= 0 && index < Questions.Count - 1)
        {
            Questions.Move(index, index + 1);
        }
    }

    /// <summary>Formdaki tarih ve saat bilgisayarın yerel saatidir; sunucuya mutlak an olarak gider.</summary>
    private static DateTimeOffset? ToLocalInstant(DateTime? date, TimeSpan? time) =>
        date is { } d && time is { } t
            ? new DateTimeOffset(DateTime.SpecifyKind(d.Date + t, DateTimeKind.Local))
            : null;

    private static (DateTime? Date, TimeSpan? Time) ToLocalParts(DateTimeOffset instant)
    {
        var local = instant.ToLocalTime();
        return (local.Date, local.TimeOfDay);
    }

    private async Task RunAsync(Func<Task> action)
    {
        ErrorMessage = null;
        try
        {
            await action();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
