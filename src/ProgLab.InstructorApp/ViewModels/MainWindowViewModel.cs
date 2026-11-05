using CommunityToolkit.Mvvm.ComponentModel;

namespace ProgLab.InstructorApp.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    // [ObservableProperty] bu alandan arayüze bağlanabilen "Title" özelliğini üretir.
    [ObservableProperty]
    private string _title = "Prog. Lab. Sınav Platformu — Eğitmen";
}
