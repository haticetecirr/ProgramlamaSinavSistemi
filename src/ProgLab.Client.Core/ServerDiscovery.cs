using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using ProgLab.Shared;

namespace ProgLab.Client.Core;

public interface IServerDiscovery
{
    /// <summary>İptal edilene kadar duyuruları dinler; her geçerli duyuruda onFound çağrılır.</summary>
    Task ListenAsync(Action<DiscoveryMessage> onFound, CancellationToken ct);
}

/// <summary>Sunucunun UDP duyurularını dinler. UDP engelliyse bir şey bulunmaz; kullanıcı elle IP girer.</summary>
public sealed class ServerDiscovery(int udpPort = DiscoveryMessage.DefaultUdpPort) : IServerDiscovery
{
    public async Task ListenAsync(Action<DiscoveryMessage> onFound, CancellationToken ct)
    {
        using var client = new UdpClient(AddressFamily.InterNetwork);

        // Aynı bilgisayarda birden çok öğrenci penceresi de dinleyebilsin.
        client.Client.ExclusiveAddressUse = false;
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, udpPort));

        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await client.ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                continue; // Geçici ağ hatası; dinlemeye devam.
            }

            if (DiscoveryMessage.TryParse(Encoding.UTF8.GetString(received.Buffer), out var message))
            {
                onFound(message!);
            }
        }
    }
}

public static class NetworkAddresses
{
    /// <summary>Bu bilgisayarın ağ adresleri; eğitmen ekranında "öğrenciler buraya bağlanır" diye gösterilir.</summary>
    public static IReadOnlyList<string> GetLocalIPv4() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) && !a.Address.ToString().StartsWith("169.254."))
            .Select(a => a.Address.ToString())
            .ToList();
}
