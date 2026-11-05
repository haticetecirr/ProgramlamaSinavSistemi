using CommunityToolkit.Mvvm.ComponentModel;

namespace ProgLab.StudentApp.ViewModels;

/// <summary>Girişten sonraki geçici ekran. Sınav lobisi (LobbyView) Aşama 6'da bunun yerini alır.</summary>
public sealed partial class HomeViewModel(string fullName) : ObservableObject
{
    public string Greeting { get; } = $"Hoş geldiniz, {fullName}.";
}
