using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;

namespace ProgLab.InstructorApp.ViewModels;

/// <summary>Geçici parolayla ilk girişte açılır; parola değişmeden diğer ekranlara geçilemez.</summary>
public sealed partial class ChangePasswordViewModel(ApiClient api, Action onChanged) : ObservableObject
{
    [ObservableProperty]
    private string _currentPassword = "";

    [ObservableProperty]
    private string _newPassword = "";

    [ObservableProperty]
    private string _newPasswordRepeat = "";

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task ChangeAsync()
    {
        ErrorMessage = null;
        if (NewPassword != NewPasswordRepeat)
        {
            ErrorMessage = "Yeni parolalar birbiriyle aynı değil.";
            return;
        }

        if (string.IsNullOrEmpty(CurrentPassword) || string.IsNullOrEmpty(NewPassword))
        {
            ErrorMessage = "Tüm alanları doldurun.";
            return;
        }

        try
        {
            await api.ChangePasswordAsync(CurrentPassword, NewPassword);
            onChanged();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
