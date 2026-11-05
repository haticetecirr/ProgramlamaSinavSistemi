using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.InstructorApp.ViewModels;

public sealed partial class LoginViewModel(ApiClient api, Action<LoginResponse> onLoggedIn, string? initialMessage = null) : ObservableObject
{
    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string? _errorMessage = initialMessage;

    [RelayCommand]
    private async Task LoginAsync()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Kullanıcı adı ve parolayı girin.";
            return;
        }

        try
        {
            var response = await api.LoginAsync(Username.Trim(), Password, UserRole.Instructor);
            Password = "";
            onLoggedIn(response);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
