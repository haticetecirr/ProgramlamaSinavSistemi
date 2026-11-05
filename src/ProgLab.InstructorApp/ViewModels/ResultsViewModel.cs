using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Sonuç tablosu satırı. Katılmayanda ve çözmeye devam edende puan görünmez.</summary>
public sealed record ResultRow(ExamResultRowDto Result, int TotalQuestions)
{
    public string Username => Result.Username;

    public string FullName => Result.FullName;

    public string StatusText => Result.Status switch
    {
        SubmissionStatus.Submitted => "Teslim etti",
        SubmissionStatus.AutoSubmitted => "Otomatik teslim",
        SubmissionStatus.InProgress => "Çözüyor",
        _ => "Katılmadı"
    };

    public string SubmittedText => Result.SubmittedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "—";

    public string CorrectText => Result.CorrectCount is { } correct ? $"{correct}/{TotalQuestions}" : "—";

    public string ScoreText => Result.TotalScore?.ToString("0.##") ?? "—";
}

public enum ResultSort
{
    Number,
    Name,
    Score,
    SubmittedAt
}

/// <summary>Öğrenci bazında sonuçlar; sınav sürerken de açılabilir.</summary>
public sealed partial class ResultsViewModel(ApiClient api, int examId, Action<long> openSubmission, Action onBack) : ObservableObject
{
    private static readonly CultureInfo Turkish = new("tr-TR");

    private List<ResultRow> _all = [];

    public ObservableCollection<ResultRow> Rows { get; } = [];

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _summaryText = "";

    [ObservableProperty]
    private ResultRow? _selectedRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortByNumber), nameof(SortByName), nameof(SortByScore), nameof(SortBySubmittedAt))]
    private ResultSort _sort = ResultSort.Number;

    [ObservableProperty]
    private string? _errorMessage;

    public bool SortByNumber
    {
        get => Sort == ResultSort.Number;
        set => SelectSort(value, ResultSort.Number);
    }

    public bool SortByName
    {
        get => Sort == ResultSort.Name;
        set => SelectSort(value, ResultSort.Name);
    }

    public bool SortByScore
    {
        get => Sort == ResultSort.Score;
        set => SelectSort(value, ResultSort.Score);
    }

    public bool SortBySubmittedAt
    {
        get => Sort == ResultSort.SubmittedAt;
        set => SelectSort(value, ResultSort.SubmittedAt);
    }

    partial void OnSortChanged(ResultSort value) => ApplySort();

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var results = await api.GetExamResultsAsync(examId);
            Title = results.Title;
            _all = results.Rows.Select(r => new ResultRow(r, results.TotalQuestions)).ToList();
            ApplySort();

            var joined = results.Rows.Count(r => r.Status is not null);
            var done = results.Rows.Count(r => r.Status is SubmissionStatus.Submitted or SubmissionStatus.AutoSubmitted);
            var state = results.Status == ExamStatus.Published ? " · sınav sürüyor" : "";
            SummaryText = $"{results.Rows.Count} öğrenci · {joined} katıldı · {done} teslim · sınav toplamı {results.MaxScore:0.##} puan{state}";
            ErrorMessage = null;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenSubmission()
    {
        if (SelectedRow is null)
        {
            ErrorMessage = "Önce listeden bir öğrenci seçin.";
        }
        else if (SelectedRow.Result.SubmissionId is { } id)
        {
            openSubmission(id);
        }
        else
        {
            ErrorMessage = "Bu öğrenci sınava katılmadı.";
        }
    }

    [RelayCommand]
    private void Back() => onBack();

    private void SelectSort(bool isSelected, ResultSort sort)
    {
        if (isSelected)
        {
            Sort = sort;
        }
    }

    private void ApplySort()
    {
        IEnumerable<ResultRow> ordered = Sort switch
        {
            ResultSort.Name => _all.OrderBy(r => r.FullName, StringComparer.Create(Turkish, CompareOptions.None)),

            // Puanı veya teslim saati olmayanlar sona gider.
            ResultSort.Score => _all.OrderByDescending(r => r.Result.TotalScore.HasValue).ThenByDescending(r => r.Result.TotalScore).ThenBy(r => r.Username, StringComparer.Ordinal),
            ResultSort.SubmittedAt => _all.OrderByDescending(r => r.Result.SubmittedAt.HasValue).ThenBy(r => r.Result.SubmittedAt).ThenBy(r => r.Username, StringComparer.Ordinal),
            _ => _all.OrderBy(r => r.Username, StringComparer.Ordinal)
        };

        Rows.Clear();
        foreach (var row in ordered)
        {
            Rows.Add(row);
        }
    }
}
