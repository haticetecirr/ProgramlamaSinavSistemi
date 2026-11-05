using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ProgLab.StudentApp.ViewModels;

/// <summary>Teslim sonrası ekran.</summary>
public sealed partial class SubmittedViewModel(ExamFinishedInfo info, Action onBack) : ObservableObject
{
    public string Title { get; } = info.Title;

    public string Headline { get; } = info.Headline;

    public string? Detail { get; } = info.Detail;

    [RelayCommand]
    private void BackToLobby() => onBack();
}
