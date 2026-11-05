using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Kategori ekleme, yeniden adlandırma ve taşıma penceresi.</summary>
public sealed partial class CategoryDialogViewModel : ObservableObject
{
    private static readonly CategoryOption NoParent = new(null, "(Üst kategori yok)");

    private readonly ApiClient _api;
    private readonly CategoryDto? _editing;
    private readonly Action<string?> _onClosed;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private CategoryOption? _selectedParent;

    [ObservableProperty]
    private string? _errorMessage;

    /// <param name="editing">Boşsa yeni kategori eklenir.</param>
    /// <param name="preselectedParentId">Yeni kategori için önerilen üst kategori.</param>
    /// <param name="onClosed">Kapanınca çağrılır; kayıt yapıldıysa bilgi mesajını alır.</param>
    public CategoryDialogViewModel(ApiClient api, IReadOnlyList<CategoryDto> tree, CategoryDto? editing, int? preselectedParentId, Action<string?> onClosed)
    {
        _api = api;
        _editing = editing;
        _onClosed = onClosed;
        _name = editing?.Name ?? "";

        Parents = new ObservableCollection<CategoryOption>(CategoryOption.Flatten(tree).Prepend(NoParent));
        var parentId = editing is null ? preselectedParentId : editing.ParentId;
        _selectedParent = Parents.FirstOrDefault(p => p.Id == parentId) ?? NoParent;
    }

    public string Heading => _editing is null ? "Yeni Kategori" : "Kategoriyi Düzenle";

    public ObservableCollection<CategoryOption> Parents { get; }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Kategori adını girin.";
            return;
        }

        try
        {
            if (_editing is null)
            {
                await _api.CreateCategoryAsync(Name, SelectedParent?.Id);
                _onClosed($"\"{Name.Trim()}\" kategorisi eklendi.");
            }
            else
            {
                await _api.UpdateCategoryAsync(_editing.Id, Name, SelectedParent?.Id);
                _onClosed($"\"{Name.Trim()}\" kategorisi kaydedildi.");
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancel() => _onClosed(null);
}
