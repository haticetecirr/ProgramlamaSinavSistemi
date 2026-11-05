using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ProgLab.Client.Core;
using ProgLab.InstructorApp.ViewModels;
using ProgLab.InstructorApp.Views;

namespace ProgLab.InstructorApp;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var http = new HttpClient { BaseAddress = new Uri(ServerAddress.Default) };
            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel(new ApiClient(http)) };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
