using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Çoktan seçmeli sorunun bir seçenek satırı.</summary>
public sealed partial class OptionRow(Action<OptionRow> remove) : ObservableObject
{
    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private bool _isCorrect;

    [RelayCommand]
    private void Remove() => remove(this);
}

/// <summary>Klasik sorunun bir kabul edilen cevap satırı.</summary>
public sealed partial class AnswerKeyRow(Action<AnswerKeyRow> remove) : ObservableObject
{
    [ObservableProperty]
    private string _text = "";

    [RelayCommand]
    private void Remove() => remove(this);
}

/// <summary>Soru ekleme ve düzenleme formu; soru tipine göre seçenekleri veya kabul edilen cevapları gösterir.</summary>
public sealed partial class QuestionEditorViewModel : ObservableObject
{
    public const int MinOptions = 2;
    public const int MaxOptions = 6;
    private const decimal DefaultPointsValue = 10m;

    private readonly ApiClient _api;
    private readonly int? _questionId;
    private readonly Action<string?> _onClosed;

    public QuestionEditorViewModel(ApiClient api, int? questionId, Action<string?> onClosed)
    {
        _api = api;
        _questionId = questionId;
        _onClosed = onClosed;

        if (questionId is null)
        {
            // Yeni soru: boş bir başlangıç formu.
            AddOptionRow();
            AddOptionRow();
            AddAnswerKeyRow();
        }
    }

    public ObservableCollection<CategoryOption> Categories { get; } = [];

    public ObservableCollection<OptionRow> Options { get; } = [];

    public ObservableCollection<AnswerKeyRow> AnswerKeys { get; } = [];

    /// <summary>Doküman gereği eğitmene gösterilen klasik puanlama sınırı.</summary>
    public string ClassicGradingNote { get; } =
        "Klasik sorular kabul edilen cevaplarla birebir karşılaştırılır; farklı ifade edilmiş doğru cevaplar için puanı elle düzeltebilirsiniz.";

    public string Heading => _questionId is null ? "Yeni Soru" : "Soruyu Düzenle";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private decimal? _defaultPoints = DefaultPointsValue;

    [ObservableProperty]
    private CategoryOption? _selectedCategory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClassic))]
    private bool _isMultipleChoice = true;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsClassic
    {
        get => !IsMultipleChoice;
        set => IsMultipleChoice = !value;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var tree = await _api.GetCategoriesAsync();
            Categories.Clear();
            foreach (var option in CategoryOption.Flatten(tree))
            {
                Categories.Add(option);
            }

            if (_questionId is not { } id)
            {
                SelectedCategory = Categories.FirstOrDefault();
                return;
            }

            var question = await _api.GetQuestionAsync(id);
            Title = question.Title;
            Text = question.Text;
            DefaultPoints = question.DefaultPoints;
            IsMultipleChoice = question.Type == QuestionType.MultipleChoice;
            IsLocked = question.IsLocked;
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == question.CategoryId);

            Options.Clear();
            foreach (var option in question.Options)
            {
                Options.Add(new OptionRow(RemoveOptionRow) { Text = option.Text, IsCorrect = option.IsCorrect });
            }

            AnswerKeys.Clear();
            foreach (var key in question.AnswerKeys)
            {
                AnswerKeys.Add(new AnswerKeyRow(RemoveAnswerKeyRow) { Text = key.AcceptedText });
            }

            // Tip değiştirilirse diğer tipin formu boş kalmasın.
            while (Options.Count < MinOptions)
            {
                AddOptionRow();
            }

            if (AnswerKeys.Count == 0)
            {
                AddAnswerKeyRow();
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void AddOption()
    {
        if (Options.Count >= MaxOptions)
        {
            ErrorMessage = "Çoktan seçmeli soruda en fazla 6 seçenek olabilir.";
            return;
        }

        AddOptionRow();
    }

    [RelayCommand]
    private void AddAnswerKey() => AddAnswerKeyRow();

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Text) || SelectedCategory?.Id is null)
        {
            ErrorMessage = "Başlık, soru metni ve kategori zorunludur.";
            return;
        }

        if (IsMultipleChoice && Options.Any(o => string.IsNullOrWhiteSpace(o.Text)))
        {
            ErrorMessage = "Seçenek metinleri boş olamaz. Fazla satırı kaldırın.";
            return;
        }

        var request = new SaveQuestionRequest(
            SelectedCategory.Id.Value,
            IsMultipleChoice ? QuestionType.MultipleChoice : QuestionType.Classic,
            Title,
            Text,
            DefaultPoints ?? 0,
            IsMultipleChoice ? Options.Select(o => new QuestionOptionInput(o.Text, o.IsCorrect)).ToList() : null,
            IsMultipleChoice ? null : AnswerKeys.Select(k => k.Text).ToList());

        try
        {
            if (_questionId is not { } id)
            {
                await _api.CreateQuestionAsync(request);
                _onClosed($"\"{Title.Trim()}\" eklendi.");
                return;
            }

            var response = await _api.UpdateQuestionAsync(id, request);
            _onClosed(response.NewVersionId is null
                ? $"\"{Title.Trim()}\" kaydedildi."
                : $"Yeni sürüm oluşturuldu (sürüm {response.Question.VersionNo}). Yayınlanmış sınavlar eski sürümü kullanmaya devam eder.");
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancel() => _onClosed(null);

    private void AddOptionRow() => Options.Add(new OptionRow(RemoveOptionRow));

    private void RemoveOptionRow(OptionRow row) => Options.Remove(row);

    private void AddAnswerKeyRow() => AnswerKeys.Add(new AnswerKeyRow(RemoveAnswerKeyRow));

    private void RemoveAnswerKeyRow(AnswerKeyRow row) => AnswerKeys.Remove(row);
}
