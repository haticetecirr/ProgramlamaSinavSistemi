using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;

namespace ProgLab.StudentApp.ViewModels;

/// <summary>Ana pencere: hangi ekranın gösterileceğine CurrentPage karar verir.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ApiClient _api;

    [ObservableProperty]
    private string _title = "Prog. Lab. Sınav Platformu — Öğrenci";

    [ObservableProperty]
    private ObservableObject _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn))]
    private string? _userName;

    public MainWindowViewModel(ApiClient api)
    {
        _api = api;
        _api.SessionExpired += () => ShowLogin("Oturum süreniz doldu. Lütfen yeniden giriş yapın.");
        _currentPage = CreateLogin();
    }

    public bool IsLoggedIn => UserName is not null;

    [RelayCommand]
    private void Logout()
    {
        _api.Logout();
        ShowLogin();
    }

    private LoginViewModel CreateLogin(string? message = null) => new(_api, OnLoggedIn, ShowFirstLogin, message);

    private void ShowLogin(string? message = null)
    {
        UserName = null;
        CurrentPage = CreateLogin(message);
    }

    private void ShowFirstLogin() => CurrentPage = new FirstLoginViewModel(_api, OnLoggedIn, () => ShowLogin());

    private void OnLoggedIn(LoginResponse response)
    {
        UserName = response.FullName;
        CurrentPage = new HomeViewModel(response.FullName);
    }
}
