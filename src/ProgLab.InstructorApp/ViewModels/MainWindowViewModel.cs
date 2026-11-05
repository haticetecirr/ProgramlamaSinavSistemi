using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Ana pencere: hangi ekranın gösterileceğine CurrentPage karar verir.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ApiClient _api;

    [ObservableProperty]
    private string _title = "Prog. Lab. Sınav Platformu — Eğitmen";

    [ObservableProperty]
    private ObservableObject _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn))]
    private string? _userName;

    /// <summary>Parola değiştirme zorunluluğu varken menü gizlenir.</summary>
    [ObservableProperty]
    private bool _canNavigate;

    public MainWindowViewModel(ApiClient api)
    {
        _api = api;
        _api.SessionExpired += () => ShowLogin("Oturum süreniz doldu. Lütfen yeniden giriş yapın.");
        _currentPage = new LoginViewModel(_api, OnLoggedIn);
    }

    public bool IsLoggedIn => UserName is not null;

    [RelayCommand]
    private void Logout()
    {
        _api.Logout();
        ShowLogin();
    }

    [RelayCommand]
    private void ShowClasses()
    {
        CanNavigate = true;
        var page = new ClassesViewModel(_api);
        CurrentPage = page;
        _ = page.LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void ShowQuestionBank() => OpenQuestionBank(null);

    private void OpenQuestionBank(string? message)
    {
        var page = new QuestionBankViewModel(_api, OpenQuestionEditor, message);
        CurrentPage = page;
        _ = page.LoadCommand.ExecuteAsync(null);
    }

    private void OpenQuestionEditor(int? questionId)
    {
        var page = new QuestionEditorViewModel(_api, questionId, OpenQuestionBank);
        CurrentPage = page;
        _ = page.LoadCommand.ExecuteAsync(null);
    }

    private void ShowLogin(string? message = null)
    {
        UserName = null;
        CanNavigate = false;
        CurrentPage = new LoginViewModel(_api, OnLoggedIn, message);
    }

    private void OnLoggedIn(LoginResponse response)
    {
        UserName = response.FullName;
        if (response.MustChangePassword)
        {
            CurrentPage = new ChangePasswordViewModel(_api, ShowClasses);
        }
        else
        {
            ShowClasses();
        }
    }
}
