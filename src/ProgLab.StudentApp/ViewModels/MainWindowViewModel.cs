using CommunityToolkit.Mvvm.ComponentModel;

namespace ProgLab.StudentApp.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    // [ObservableProperty] bu alandan arayüze bağlanabilen "Title" özelliğini üretir.
    [ObservableProperty]
    private string _title = "Prog. Lab. Sınav Platformu — Öğrenci";
}
