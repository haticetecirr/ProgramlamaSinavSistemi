using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Gönderimdeki bir soru; klasik soruda puan düzeltme alanları vardır.</summary>
public sealed partial class QuestionResultRow(QuestionResultDto question, Func<QuestionResultRow, Task> onSave) : ObservableObject
{
    public QuestionResultDto Question { get; } = question;

    public string Heading { get; } = $"Soru {question.OrderNo} · {question.Points:0.##} puan";

    public string Text { get; } = question.Text;

    public string AnswerText { get; } = string.IsNullOrWhiteSpace(question.AnswerText) ? "(boş)" : question.AnswerText;

    public string CorrectAnswer { get; } = question.CorrectAnswer;

    public bool CanOverride { get; } = question.CanOverride;

    public string SaveName { get; } = $"Puanı kaydet: Soru {question.OrderNo}";

    public string ScoreName { get; } = $"Puan: Soru {question.OrderNo}";

    public string NoteName { get; } = $"Not: Soru {question.OrderNo}";

    [ObservableProperty]
    private string _scoreText = FormatScore(question.Score, question.Points, question.GradedBy);

    [ObservableProperty]
    private string? _noteText = question.GradedNote;

    [ObservableProperty]
    private string _scoreInput = question.Score?.ToString("0.##", CultureInfo.InvariantCulture) ?? "0";

    [ObservableProperty]
    private string? _noteInput = question.GradedNote;

    public void ShowOverride(decimal score, string? note)
    {
        ScoreText = FormatScore(score, Question.Points, GradedBy.Manual);
        NoteText = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    [RelayCommand]
    private Task SaveAsync() => onSave(this);

    private static string FormatScore(decimal? score, decimal points, GradedBy? by)
    {
        var source = by == GradedBy.Manual ? " · elle düzeltildi" : "";
        return score is { } value ? $"{value:0.##} / {points:0.##}{source}" : $"— / {points:0.##}";
    }
}

/// <summary>Tek öğrencinin soru bazında cevapları ve elle puan düzeltme.</summary>
public sealed partial class SubmissionDetailViewModel(ApiClient api, long submissionId, Action onBack) : ObservableObject
{
    private decimal _maxScore;

    public ObservableCollection<QuestionResultRow> Questions { get; } = [];

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _studentText = "";

    [ObservableProperty]
    private string _totalText = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    public string GradingHint => "Klasik sorular kabul edilen cevaplarla birebir karşılaştırılır; farklı ifade edilmiş doğru cevaplar için puanı elle düzeltebilirsiniz.";

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var detail = await api.GetSubmissionDetailAsync(submissionId);
            Title = detail.ExamTitle;
            _maxScore = detail.MaxScore;
            var state = detail.Status switch
            {
                SubmissionStatus.Submitted => "Teslim etti",
                SubmissionStatus.AutoSubmitted => "Otomatik teslim",
                _ => "Çözüyor"
            };
            var time = detail.SubmittedAt is { } at ? $" · {at.ToLocalTime():dd.MM.yyyy HH:mm:ss}" : "";
            StudentText = $"{detail.Username} · {detail.FullName} · {state}{time}";
            ShowTotal(detail.TotalScore);

            Questions.Clear();
            foreach (var question in detail.Questions)
            {
                Questions.Add(new QuestionResultRow(question, SaveScoreAsync));
            }

            ErrorMessage = null;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Back() => onBack();

    private async Task SaveScoreAsync(QuestionResultRow row)
    {
        ErrorMessage = null;
        StatusMessage = null;

        // Türkçe klavyede virgül de yazılabilir.
        if (!decimal.TryParse(row.ScoreInput?.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var score))
        {
            ErrorMessage = "Puan sayı olmalıdır.";
            return;
        }

        try
        {
            var result = await api.OverrideScoreAsync(row.Question.AnswerId!.Value, new OverrideScoreRequest(score, row.NoteInput));
            row.ShowOverride(result.Score, row.NoteInput);
            ShowTotal(result.TotalScore);
            StatusMessage = $"Soru {row.Question.OrderNo} puanı düzeltildi.";
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void ShowTotal(decimal? total) =>
        TotalText = total is { } value ? $"Toplam: {value:0.##} / {_maxScore:0.##}" : $"Toplam: — / {_maxScore:0.##}";
}
