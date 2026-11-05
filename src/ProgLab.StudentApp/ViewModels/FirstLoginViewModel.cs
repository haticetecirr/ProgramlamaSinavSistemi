using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;

namespace ProgLab.StudentApp.ViewModels;

/// <summary>Öğrenci numarası ve kayıtlı e-postayla doğrulanıp parola belirlenen ekran.</summary>
public sealed partial class FirstLoginViewModel(ApiClient api, Action<LoginResponse> onLoggedIn, Action onBack) : ObservableObject
{
    [ObservableProperty]
    private string _studentNumber = "";

    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _newPassword = "";

    [ObservableProperty]
    private string _newPasswordRepeat = "";

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task SubmitAsync()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(StudentNumber) || string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(NewPassword))
        {
            ErrorMessage = "Tüm alanları doldurun.";
            return;
        }

        if (NewPassword != NewPasswordRepeat)
        {
            ErrorMessage = "Parolalar birbiriyle aynı değil.";
            return;
        }

        try
        {
            onLoggedIn(await api.FirstLoginAsync(StudentNumber.Trim(), Email.Trim(), NewPassword));
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Back() => onBack();
}
