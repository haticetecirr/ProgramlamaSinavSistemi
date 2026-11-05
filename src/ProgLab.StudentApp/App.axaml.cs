using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ProgLab.Client.Core;
using ProgLab.StudentApp.ViewModels;
using ProgLab.StudentApp.Views;

namespace ProgLab.StudentApp;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = LocalClientStore.ForApp("StudentApp");
            var main = new MainWindowViewModel(
                ServerSessionFactory.Pinned(store),
                new ServerDiscovery(),
                store,
                action => Dispatcher.UIThread.Post(action)); // Ağdan gelen olaylar arayüz iş parçacığına taşınır.
            desktop.MainWindow = new MainWindow { DataContext = main };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
