using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.StudentApp.ViewModels;

/// <summary>
/// Sınav ekranındaki bir soru. Ekran soru tipine göre DataTemplate seçer;
/// finalde kod sorusu için yeni bir alt sınıf ve şablon eklenir, mevcut kod değişmez.
/// </summary>
public abstract partial class PaperQuestionViewModel(StudentQuestionDto question, int number) : ObservableObject
{
    public int ExamQuestionId { get; } = question.ExamQuestionId;

    public int Number { get; } = number;

    public string Text { get; } = question.Text;

    public string Heading { get; } = $"Soru {number} · {question.Points:0.##} puan";

    public string ListTitle => $"Soru {Number}";

    public string StateText => IsAnswered ? "Cevaplandı" : "Boş";

    public abstract bool IsAnswered { get; }

    /// <summary>Sunucuya gidecek cevap.</summary>
    public abstract SaveAnswerRequest ToSaveRequest();

    protected void NotifyAnswerChanged(Action<PaperQuestionViewModel>? onChanged)
    {
        OnPropertyChanged(nameof(IsAnswered));
        OnPropertyChanged(nameof(StateText));
        onChanged?.Invoke(this);
    }

    public static PaperQuestionViewModel Create(StudentQuestionDto question, int number, StudentAnswerDto? saved, Action<PaperQuestionViewModel>? onChanged) => question.Type switch
    {
        QuestionType.MultipleChoice => new MultipleChoiceQuestionViewModel(question, number, saved, onChanged),
        QuestionType.Classic => new ClassicQuestionViewModel(question, number, saved, onChanged),
        _ => throw new NotSupportedException($"Desteklenmeyen soru tipi: {question.Type}")
    };
}

/// <summary>Bir şık. A, B, C harfi sırasından türetilir.</summary>
public sealed partial class PaperOptionViewModel(StudentOptionDto option, Action<PaperOptionViewModel> onSelected) : ObservableObject
{
    public int Id { get; } = option.Id;

    public string Text { get; } = $"{(char)('A' + option.OrderNo - 1)}) {option.Text}";

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            onSelected(this);
        }
    }
}

public sealed partial class MultipleChoiceQuestionViewModel : PaperQuestionViewModel
{
    private readonly Action<PaperQuestionViewModel>? _onChanged;
    private bool _isApplying;

    public MultipleChoiceQuestionViewModel(StudentQuestionDto question, int number, StudentAnswerDto? saved, Action<PaperQuestionViewModel>? onChanged)
        : base(question, number)
    {
        _onChanged = onChanged;
        Options = question.Options.Select(o => new PaperOptionViewModel(o, OnOptionSelected)).ToList();

        _isApplying = true;
        foreach (var option in Options)
        {
            option.IsSelected = option.Id == saved?.SelectedOptionId;
        }

        _isApplying = false;
    }

    public IReadOnlyList<PaperOptionViewModel> Options { get; }

    public int? SelectedOptionId => Options.FirstOrDefault(o => o.IsSelected)?.Id;

    public override bool IsAnswered => SelectedOptionId is not null;

    public override SaveAnswerRequest ToSaveRequest() => new(SelectedOptionId, null);

    /// <summary>Radyo düğmesi kendi başına boşaltılamaz; işareti kaldırmak için bu komut kullanılır.</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        _isApplying = true;
        foreach (var option in Options)
        {
            option.IsSelected = false;
        }

        _isApplying = false;
        NotifyAnswerChanged(_onChanged);
    }

    private void OnOptionSelected(PaperOptionViewModel selected)
    {
        if (_isApplying)
        {
            return;
        }

        // Tek seçenek işaretli kalır.
        _isApplying = true;
        foreach (var option in Options.Where(o => o != selected))
        {
            option.IsSelected = false;
        }

        _isApplying = false;
        NotifyAnswerChanged(_onChanged);
    }
}

public sealed partial class ClassicQuestionViewModel(StudentQuestionDto question, int number, StudentAnswerDto? saved, Action<PaperQuestionViewModel>? onChanged)
    : PaperQuestionViewModel(question, number)
{
    public const int MaxLength = 2000;

    [ObservableProperty]
    private string _textAnswer = saved?.TextAnswer ?? "";

    public override bool IsAnswered => !string.IsNullOrWhiteSpace(TextAnswer);

    public override SaveAnswerRequest ToSaveRequest() => new(null, TextAnswer);

    partial void OnTextAnswerChanged(string value) => NotifyAnswerChanged(onChanged);
}
