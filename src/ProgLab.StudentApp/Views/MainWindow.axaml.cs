using Avalonia.Controls;
using ProgLab.StudentApp.ViewModels;

namespace ProgLab.StudentApp.Views;

public sealed partial class MainWindow : Window
{
    private bool _canClose;

    public MainWindow() => InitializeComponent();

    /// <summary>Pencere kapatılırken son yazılan cevap gönderilir; gönderilemezse öğrenci önce uyarılır.</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_canClose || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        e.Cancel = true;
        if (await viewModel.CanCloseAsync())
        {
            _canClose = true;
            Close();
        }
    }
}
