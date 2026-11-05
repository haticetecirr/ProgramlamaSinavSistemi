using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgLab.Client.Core;
using ProgLab.Shared;

namespace ProgLab.StudentApp.ViewModels;

/// <summary>Ağda bulunan sunucu satırı.</summary>
public sealed record DiscoveredServerRow(DiscoveryMessage Server, DateTimeOffset LastSeen)
{
    public string Title => $"{Server.Ip}:{Server.Port}";

    public string Details => $"Sürüm {Server.Version} · Parmak izi {FingerprintText.Short(Server.Fingerprint)}";
}

public static class FingerprintText
{
    /// <summary>Tahtayla karşılaştırmak için kısa biçim: ilk ve son 8 karakter.</summary>
    public static string Short(string fingerprint) =>
        fingerprint.Length <= 16 ? fingerprint : $"{fingerprint[..8]}…{fingerprint[^8..]}";
}

/// <summary>Sunucuyu bulma: UDP duyurusu, yedek olarak elle IP. Son adres hatırlanır.</summary>
public sealed partial class ConnectViewModel : ObservableObject
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(10);

    private readonly IServerSessionFactory _factory;
    private readonly IServerDiscovery _discovery;
    private readonly LocalClientStore _store;
    private readonly Action<ServerSession> _onConnected;
    private readonly Action<Action> _ui;
    private CancellationTokenSource? _listening;
    private Uri? _rejectedAddress;

    public ConnectViewModel(
        IServerSessionFactory factory,
        IServerDiscovery discovery,
        LocalClientStore store,
        Action<ServerSession> onConnected,
        Action<Action> ui)
    {
        _factory = factory;
        _discovery = discovery;
        _store = store;
        _onConnected = onConnected;
        _ui = ui;
        _manualAddress = store.LastServer ?? "";
    }

    public ObservableCollection<DiscoveredServerRow> Servers { get; } = [];

    [ObservableProperty]
    private DiscoveredServerRow? _selectedServer;

    [ObservableProperty]
    private string _manualAddress;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Sertifika değiştiyse kullanıcı, eğitmene sorduktan sonra kaydı silebilir.</summary>
    [ObservableProperty]
    private bool _canForgetCertificate;

    public void StartDiscovery()
    {
        StopDiscovery();
        var cts = _listening = new CancellationTokenSource();
        _ = _discovery.ListenAsync(message => _ui(() => OnServerFound(message)), cts.Token);
    }

    public void StopDiscovery()
    {
        _listening?.Cancel();
        _listening = null;
    }

    /// <summary>Bir süredir duyuru gelmeyen sunucuları listeden kaldırır.</summary>
    public void RemoveStaleServers(DateTimeOffset now)
    {
        foreach (var row in Servers.Where(s => now - s.LastSeen > StaleAfter).ToList())
        {
            Servers.Remove(row);
        }
    }

    [RelayCommand]
    private Task ConnectSelectedAsync()
    {
        if (SelectedServer is not { } row)
        {
            ErrorMessage = "Listeden bir sunucu seçin veya adresi elle girin.";
            return Task.CompletedTask;
        }

        return ConnectAsync(new Uri($"https://{row.Server.Ip}:{row.Server.Port}/"), row.Server.Fingerprint, row.Title);
    }

    [RelayCommand]
    private Task ConnectManualAsync()
    {
        if (ServerAddress.FromInput(ManualAddress) is not { } address)
        {
            ErrorMessage = "Geçerli bir adres girin. Örnek: 192.168.1.10";
            CanForgetCertificate = false;
            return Task.CompletedTask;
        }

        return ConnectAsync(address, null, ManualAddress.Trim());
    }

    [RelayCommand]
    private void ForgetCertificate()
    {
        if (_rejectedAddress is { } address)
        {
            _store.RemovePin(ServerAddress.Key(address));
        }

        CanForgetCertificate = false;
        ErrorMessage = null;
        StatusMessage = "Kayıtlı sertifika silindi. Yeniden bağlanabilirsiniz.";
    }

    private async Task ConnectAsync(Uri address, string? expectedFingerprint, string addressText)
    {
        ErrorMessage = null;
        StatusMessage = null;
        CanForgetCertificate = false;

        var session = _factory.Create(address, expectedFingerprint);
        try
        {
            await session.Api.GetHealthAsync();
        }
        catch (ApiException ex)
        {
            if (session.Pinner?.Rejected == true)
            {
                _rejectedAddress = address;
                CanForgetCertificate = true;
                ErrorMessage = "Bu sunucunun güvenlik sertifikası değişmiş, bağlantı reddedildi. Doğru sunucu olduğundan emin değilseniz devam etmeyin; eğitmene sorun.";
            }
            else
            {
                ErrorMessage = ex.Message;
            }

            return;
        }

        _store.LastServer = addressText;
        StopDiscovery();
        _onConnected(session);
    }

    private void OnServerFound(DiscoveryMessage message)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = Servers.FirstOrDefault(s => s.Server == message);
        if (existing is not null)
        {
            var index = Servers.IndexOf(existing);
            var wasSelected = SelectedServer == existing;
            var updated = new DiscoveredServerRow(message, now);
            Servers[index] = updated;
            if (wasSelected)
            {
                SelectedServer = updated;
            }
        }
        else
        {
            Servers.Add(new DiscoveredServerRow(message, now));
        }

        RemoveStaleServers(now);
    }
}
