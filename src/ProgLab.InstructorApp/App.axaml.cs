using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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
            // Sunucu aynı bilgisayarda çalışır; uygulama da aynı API'yi kullanır.
            var session = ServerSessionFactory.Local().Create(ServerAddress.Local);
            var main = new MainWindowViewModel(
                session.Api,
                session.Hub,
                () => session.Pinner?.Fingerprint,
                action => Dispatcher.UIThread.Post(action)); // Ağdan gelen olaylar arayüz iş parçacığına taşınır.
            desktop.MainWindow = new MainWindow { DataContext = main };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
