using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Soru bankası: kategori ağacı, filtreler ve soru listesi.</summary>
public sealed partial class QuestionBankViewModel(ApiClient api, Action<int?> openEditor, string? initialMessage = null) : ObservableObject
{
    private const int PageSize = 50;

    private IReadOnlyList<CategoryDto> _tree = [];

    // Kod seçimi değiştirirken liste ayrıca yüklenmesin; hızlı değişimde yalnızca son yükleme yazılsın.
    private bool _isReloadingCategories;
    private int _questionsLoadVersion;

    public ObservableCollection<CategoryNode> Categories { get; } = [];

    public ObservableCollection<QuestionRow> Questions { get; } = [];

    public IReadOnlyList<QuestionTypeFilter> TypeFilters { get; } =
    [
        new(null, "Tüm tipler"),
        new(QuestionType.MultipleChoice, "Çoktan seçmeli"),
        new(QuestionType.Classic, "Klasik")
    ];

    [ObservableProperty]
    private CategoryNode? _selectedCategory;

    [ObservableProperty]
    private QuestionTypeFilter? _selectedTypeFilter;

    [ObservableProperty]
    private QuestionRow? _selectedQuestion;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    private int _page = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    private int _total;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingDelete))]
    private QuestionRow? _pendingDelete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCategoryDialogOpen))]
    private CategoryDialogViewModel? _categoryDialog;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage = initialMessage;

    public string PageText => $"Sayfa {Page} / {Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize))} · {Total} soru";

    public bool IsConfirmingDelete => PendingDelete is not null;

    public bool IsCategoryDialogOpen => CategoryDialog is not null;

    partial void OnSelectedCategoryChanged(CategoryNode? value)
    {
        if (!_isReloadingCategories)
        {
            Page = 1;
            _ = RunAsync(LoadQuestionsCoreAsync, clearMessages: false);
        }
    }

    partial void OnSelectedTypeFilterChanged(QuestionTypeFilter? value)
    {
        Page = 1;
        _ = RunAsync(LoadQuestionsCoreAsync, clearMessages: false);
    }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(ReloadAllAsync, clearMessages: false);

    [RelayCommand]
    private Task SearchAsync()
    {
        Page = 1;
        return RunAsync(LoadQuestionsCoreAsync);
    }

    [RelayCommand]
    private Task ClearCategoryFilterAsync()
    {
        _isReloadingCategories = true;
        SelectedCategory = null;
        _isReloadingCategories = false;
        Page = 1;
        return RunAsync(LoadQuestionsCoreAsync);
    }

    [RelayCommand]
    private Task PreviousPageAsync()
    {
        if (Page <= 1)
        {
            return Task.CompletedTask;
        }

        Page--;
        return RunAsync(LoadQuestionsCoreAsync);
    }

    [RelayCommand]
    private Task NextPageAsync()
    {
        if (Page * PageSize >= Total)
        {
            return Task.CompletedTask;
        }

        Page++;
        return RunAsync(LoadQuestionsCoreAsync);
    }

    [RelayCommand]
    private void NewQuestion() => openEditor(null);

    [RelayCommand]
    private void EditQuestion()
    {
        if (SelectedQuestion is { } row)
        {
            openEditor(row.Question.Id);
        }
        else
        {
            ErrorMessage = "Önce listeden bir soru seçin.";
        }
    }

    [RelayCommand]
    private void AskDeleteQuestion()
    {
        if (SelectedQuestion is { } row)
        {
            ErrorMessage = null;
            PendingDelete = row;
        }
        else
        {
            ErrorMessage = "Önce listeden bir soru seçin.";
        }
    }

    [RelayCommand]
    private void CancelDeleteQuestion() => PendingDelete = null;

    [RelayCommand]
    private Task ConfirmDeleteQuestionAsync() => RunAsync(async () =>
    {
        if (PendingDelete is not { } row)
        {
            return;
        }

        PendingDelete = null;
        await api.DeleteQuestionAsync(row.Question.Id);
        StatusMessage = $"\"{row.Title}\" silindi. Daha önce yayınlanmış sınavlar bu soruyu kullanmaya devam eder.";
        await LoadQuestionsCoreAsync();
    });

    [RelayCommand]
    private void AddCategory() => OpenCategoryDialog(editing: null);

    [RelayCommand]
    private void EditCategory()
    {
        if (SelectedCategory is { } node)
        {
            OpenCategoryDialog(node.Category);
        }
        else
        {
            ErrorMessage = "Önce bir kategori seçin.";
        }
    }

    [RelayCommand]
    private Task DeleteCategoryAsync() => RunAsync(async () =>
    {
        if (SelectedCategory is not { } node)
        {
            ErrorMessage = "Önce bir kategori seçin.";
            return;
        }

        await api.DeleteCategoryAsync(node.Category.Id);
        StatusMessage = $"\"{node.Name}\" kategorisi silindi.";
        await ReloadAllAsync();
    });

    private void OpenCategoryDialog(CategoryDto? editing)
    {
        ErrorMessage = null;
        CategoryDialog = new CategoryDialogViewModel(api, _tree, editing, SelectedCategory?.Category.Id, message =>
        {
            CategoryDialog = null;
            if (message is not null)
            {
                StatusMessage = message;
                CategoryDialogSaveTask = RunAsync(ReloadAllAsync, clearMessages: false);
            }
        });
    }

    /// <summary>Kategori kaydından sonraki yenileme; testler bunun bitmesini bekleyebilir.</summary>
    public Task CategoryDialogSaveTask { get; private set; } = Task.CompletedTask;

    private async Task ReloadAllAsync()
    {
        var selectedId = SelectedCategory?.Category.Id;
        _tree = await api.GetCategoriesAsync();

        _isReloadingCategories = true;
        Categories.Clear();
        foreach (var category in _tree)
        {
            Categories.Add(new CategoryNode(category));
        }

        SelectedCategory = FindNode(Categories, selectedId);
        _isReloadingCategories = false;

        await LoadQuestionsCoreAsync();
    }

    private static CategoryNode? FindNode(IEnumerable<CategoryNode> nodes, int? id)
    {
        foreach (var node in nodes)
        {
            if (node.Category.Id == id)
            {
                return node;
            }

            if (FindNode(node.Children, id) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private async Task LoadQuestionsCoreAsync()
    {
        var version = ++_questionsLoadVersion;
        var result = await api.GetQuestionsAsync(SelectedCategory?.Category.Id, SelectedTypeFilter?.Value, SearchText, Page, PageSize);
        if (version != _questionsLoadVersion)
        {
            return;
        }

        Questions.Clear();
        foreach (var question in result.Items)
        {
            Questions.Add(new QuestionRow(question));
        }

        Total = result.Total;
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
