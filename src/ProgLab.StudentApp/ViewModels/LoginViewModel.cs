using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.StudentApp.ViewModels;

public sealed partial class LoginViewModel(
    ApiClient api,
    Action<LoginResponse> onLoggedIn,
    Action onFirstLogin,
    string? initialMessage = null) : ObservableObject
{
    [ObservableProperty]
    private string _studentNumber = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string? _errorMessage = initialMessage;

    [RelayCommand]
    private async Task LoginAsync()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(StudentNumber) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Öğrenci numarası ve parolayı girin.";
            return;
        }

        try
        {
            var response = await api.LoginAsync(StudentNumber.Trim(), Password, UserRole.Student);
            Password = "";
            onLoggedIn(response);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void GoToFirstLogin() => onFirstLogin();
}
