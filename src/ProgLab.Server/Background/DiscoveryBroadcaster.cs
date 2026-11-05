using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Options;
using ProgLab.Server.Security;
using ProgLab.Shared;

namespace ProgLab.Server.Background;

/// <summary>appsettings "Discovery" bölümü.</summary>
public sealed class DiscoveryOptions
{
    public const string Section = "Discovery";

    public bool Enabled { get; set; } = true;

    public int UdpPort { get; set; } = DiscoveryMessage.DefaultUdpPort;

    public int ServerPort { get; set; } = 5000;

    public int IntervalSeconds { get; set; } = 2;
}

/// <summary>
/// Sunucu adresini yerel ağa duyurur; öğrenci IP yazmak zorunda kalmaz.
/// Bilgisayarda birden çok ağ olabilir (WiFi, kablo, telefon erişim noktası): her biri kendi adresiyle duyurulur.
/// </summary>
public sealed class DiscoveryBroadcaster(
    IOptions<DiscoveryOptions> options,
    CertificateProvider certificate,
    ILogger<DiscoveryBroadcaster> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var fingerprint = certificate.Fingerprint;
        logger.LogInformation("Sunucu duyurusu başladı (UDP {Port}). Sertifika parmak izi: {Fingerprint}", settings.UdpPort, fingerprint);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));
        do
        {
            foreach (var (address, broadcast) in GetIPv4Networks())
            {
                await SendAsync(address, broadcast, new DiscoveryMessage(address.ToString(), settings.ServerPort, version, fingerprint), settings.UdpPort, stoppingToken);
            }
        }
        while (await WaitNextAsync(timer, stoppingToken));
    }

    /// <summary>Bir ağın yayın adresi: ör. 192.168.1.10 / 255.255.255.0 → 192.168.1.255</summary>
    public static IPAddress DirectedBroadcast(IPAddress address, IPAddress mask)
    {
        var ip = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        var result = new byte[ip.Length];
        for (var i = 0; i < ip.Length; i++)
        {
            result[i] = (byte)(ip[i] | ~maskBytes[i]);
        }

        return new IPAddress(result);
    }

    private static async Task<bool> WaitNextAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task SendAsync(IPAddress local, IPAddress broadcast, DiscoveryMessage message, int udpPort, CancellationToken ct)
    {
        try
        {
            using var client = new UdpClient(new IPEndPoint(local, 0)) { EnableBroadcast = true };
            var bytes = Encoding.UTF8.GetBytes(message.Format());
            await client.SendAsync(bytes, new IPEndPoint(broadcast, udpPort), ct);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            // Ağ o an kullanılamıyor olabilir; bir sonraki turda yeniden denenir. Öğrenci elle IP de girebilir.
            logger.LogDebug(ex, "UDP duyurusu gönderilemedi: {Address}", local);
        }
    }

    private static IEnumerable<(IPAddress Address, IPAddress Broadcast)> GetIPv4Networks() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) && !a.Address.ToString().StartsWith("169.254."))
            .Select(a => (a.Address, DirectedBroadcast(a.Address, a.IPv4Mask)));
}
