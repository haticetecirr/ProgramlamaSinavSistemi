using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Sınav listesindeki satır. Saatler bilgisayarın yerel saat diliminde gösterilir.</summary>
public sealed record ExamRow(ExamSummaryDto Exam)
{
    public string Title => Exam.Title;

    public string Details
    {
        get
        {
            var kind = Exam.Kind == ExamKind.Exam ? $"Sınav ({Exam.DurationMinutes} dk)" : "Ödev";
            var window = Exam.StartsAt is { } start && Exam.EndsAt is { } end
                ? $" · {start.ToLocalTime():dd.MM.yyyy HH:mm} – {end.ToLocalTime():dd.MM.yyyy HH:mm}"
                : "";
            return $"{kind} · {Exam.ClassName} · {Exam.QuestionCount} soru · {Exam.TotalPoints:0.##} puan{window}";
        }
    }
}

/// <summary>Sınav listesi: durum sekmeleri, yeni, düzenle, sil, bitir, kopyala.</summary>
public sealed partial class ExamListViewModel(ApiClient api, Action<int?> openEditor, string? initialMessage = null) : ObservableObject
{
    private int _loadVersion;

    public ObservableCollection<ExamRow> Exams { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDrafts), nameof(ShowPublished), nameof(ShowCompleted))]
    private ExamStatus _status = ExamStatus.Draft;

    [ObservableProperty]
    private ExamRow? _selectedExam;

    /// <summary>Onay bekleyen silme.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingDelete))]
    private ExamRow? _pendingDelete;

    /// <summary>Onay bekleyen bitirme.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingEnd))]
    private ExamRow? _pendingEnd;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage = initialMessage;

    public bool IsConfirmingDelete => PendingDelete is not null;

    public bool IsConfirmingEnd => PendingEnd is not null;

    public bool ShowDrafts
    {
        get => Status == ExamStatus.Draft;
        set => SelectTab(value, ExamStatus.Draft);
    }

    public bool ShowPublished
    {
        get => Status == ExamStatus.Published;
        set => SelectTab(value, ExamStatus.Published);
    }

    public bool ShowCompleted
    {
        get => Status == ExamStatus.Completed;
        set => SelectTab(value, ExamStatus.Completed);
    }

    /// <summary>Sekme değişiminden sonraki yükleme; testler bunun bitmesini bekleyebilir.</summary>
    public Task TabLoadTask { get; private set; } = Task.CompletedTask;

    [RelayCommand]
    private Task LoadAsync() => RunAsync(LoadCoreAsync, clearMessages: false);

    [RelayCommand]
    private void NewExam() => openEditor(null);

    [RelayCommand]
    private void OpenExam()
    {
        if (SelectedExam is { } row)
        {
            openEditor(row.Exam.Id);
        }
        else
        {
            ErrorMessage = "Önce listeden bir sınav seçin.";
        }
    }

    [RelayCommand]
    private void AskDelete()
    {
        if (SelectedExam is { } row)
        {
            ErrorMessage = null;
            PendingEnd = null;
            PendingDelete = row;
        }
        else
        {
            ErrorMessage = "Önce listeden bir sınav seçin.";
        }
    }

    [RelayCommand]
    private Task ConfirmDeleteAsync() => RunAsync(async () =>
    {
        if (PendingDelete is not { } row)
        {
            return;
        }

        PendingDelete = null;
        await api.DeleteExamAsync(row.Exam.Id);
        StatusMessage = $"\"{row.Title}\" taslağı silindi.";
        await LoadCoreAsync();
    });

    [RelayCommand]
    private void AskEnd()
    {
        if (SelectedExam is { } row)
        {
            ErrorMessage = null;
            PendingDelete = null;
            PendingEnd = row;
        }
        else
        {
            ErrorMessage = "Önce listeden bir sınav seçin.";
        }
    }

    [RelayCommand]
    private Task ConfirmEndAsync() => RunAsync(async () =>
    {
        if (PendingEnd is not { } row)
        {
            return;
        }

        PendingEnd = null;
        await api.EndExamAsync(row.Exam.Id);
        StatusMessage = $"\"{row.Title}\" bitirildi.";
        await LoadCoreAsync();
    });

    [RelayCommand]
    private void CancelConfirmation()
    {
        PendingDelete = null;
        PendingEnd = null;
    }

    [RelayCommand]
    private Task CopyAsync() => RunAsync(async () =>
    {
        if (SelectedExam is not { } row)
        {
            ErrorMessage = "Önce listeden bir sınav seçin.";
            return;
        }

        var copy = await api.CopyExamAsync(row.Exam.Id);
        StatusMessage = $"\"{copy.Title}\" taslağı oluşturuldu.";
        Status = ExamStatus.Draft;
        await LoadCoreAsync();
    });

    private void SelectTab(bool isChecked, ExamStatus status)
    {
        if (!isChecked || Status == status)
        {
            return;
        }

        Status = status;
        CancelConfirmation();
        TabLoadTask = RunAsync(LoadCoreAsync);
    }

    private async Task LoadCoreAsync()
    {
        var version = ++_loadVersion;
        var result = await api.GetExamsAsync(Status);
        if (version != _loadVersion)
        {
            return;
        }

        Exams.Clear();
        foreach (var exam in result.Items)
        {
            Exams.Add(new ExamRow(exam));
        }
    }

    private async Task RunAsync(Func<Task> action, bool clearMessages = true)
    {
        if (clearMessages)
        {
            ErrorMessage = null;
            StatusMessage = null;
        }

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
