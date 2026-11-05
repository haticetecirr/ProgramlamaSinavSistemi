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

    private void ShowLogin(string? message = null)
    {
        UserName = null;
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

    private void ShowClasses()
    {
        var page = new ClassesViewModel(_api);
        CurrentPage = page;
        _ = page.LoadCommand.ExecuteAsync(null);
    }
}
