using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared.Dtos;

namespace ProgLab.StudentApp.ViewModels;

/// <summary>Ana pencere: hangi ekranın gösterileceğine CurrentPage karar verir. Akış: bağlan → giriş → lobi.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IServerSessionFactory? _factory;
    private readonly IServerDiscovery? _discovery;
    private readonly LocalClientStore? _store;
    private readonly Action<Action> _ui;

    private ApiClient? _api;
    private ExamHubClient? _hub;
    private string? _serverText;

    [ObservableProperty]
    private string _title = "Prog. Lab. Sınav Platformu — Öğrenci";

    [ObservableProperty]
    private ObservableObject _currentPage = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn))]
    private string? _userName;

    /// <summary>Uygulamanın normal akışı: sunucu bulma ekranıyla başlar.</summary>
    public MainWindowViewModel(IServerSessionFactory factory, IServerDiscovery discovery, LocalClientStore store, Action<Action>? ui = null)
    {
        _factory = factory;
        _discovery = discovery;
        _store = store;
        _ui = ui ?? (action => action());
        ShowConnect();
    }

    /// <summary>Sunucu önceden biliniyorsa doğrudan giriş ekranıyla başlar (testler için).</summary>
    public MainWindowViewModel(ApiClient api, ExamHubClient? hub = null, Action<Action>? ui = null)
    {
        _ui = ui ?? (action => action());
        UseServer(api, hub);
        ShowLogin();
    }

    public bool IsLoggedIn => UserName is not null;

    [RelayCommand]
    private void Logout()
    {
        _api?.Logout();
        ShowLogin();
    }

    private void UseServer(ApiClient api, ExamHubClient? hub)
    {
        _api = api;
        _hub = hub;
        _api.SessionExpired += () => _ui(() => ShowLogin("Oturum süreniz doldu. Lütfen yeniden giriş yapın."));
    }

    private void LeaveCurrentPage()
    {
        switch (CurrentPage)
        {
            case LobbyViewModel lobby:
                lobby.Stop();
                break;
            case ExamPaperViewModel paper:
                paper.Stop();
                break;
            case ConnectViewModel connect:
                connect.StopDiscovery();
                break;
        }
    }

    private void ShowConnect()
    {
        LeaveCurrentPage();
        UserName = null;
        var page = new ConnectViewModel(_factory!, _discovery!, _store!, OnConnected, _ui);
        CurrentPage = page;
        page.StartDiscovery();
    }

    private void OnConnected(ServerSession session)
    {
        _serverText = $"{session.BaseAddress.Host}:{session.BaseAddress.Port}";
        UseServer(session.Api, session.Hub);
        ShowLogin();
    }

    private void ShowLogin(string? message = null)
    {
        LeaveCurrentPage();
        if (_hub is not null)
        {
            _ = _hub.StopAsync(); // Oturum kapandı: canlı bağlantı da kapanır.
        }

        UserName = null;
        CurrentPage = new LoginViewModel(_api!, OnLoggedIn, ShowFirstLogin, message, _factory is null ? null : ShowConnect, _serverText);
    }

    private void ShowFirstLogin() => CurrentPage = new FirstLoginViewModel(_api!, OnLoggedIn, () => ShowLogin());

    private void OnLoggedIn(LoginResponse response)
    {
        UserName = response.FullName;
        ShowLobby();
    }

    private void ShowLobby()
    {
        LeaveCurrentPage();
        var lobby = new LobbyViewModel(_api!, _hub, UserName!, _ui, OpenPaper);
        CurrentPage = lobby;
        lobby.Start();
    }

    private void OpenPaper(int examId)
    {
        LeaveCurrentPage();
        var paper = new ExamPaperViewModel(_api!, _hub, examId, _ui, ShowLobby);
        CurrentPage = paper;
        paper.Start();
        _ = paper.LoadCommand.ExecuteAsync(null);
    }
}
