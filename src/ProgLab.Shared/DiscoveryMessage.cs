using System.Net;

namespace ProgLab.Shared;

/// <summary>Sunucunun yerel ağa UDP ile duyurduğu mesaj: PROGLAB;&lt;ip&gt;;&lt;port&gt;;&lt;sürüm&gt;;&lt;parmak izi&gt;</summary>
public sealed record DiscoveryMessage(string Ip, int Port, string Version, string Fingerprint)
{
    public const int DefaultUdpPort = 47777;

    private const string Prefix = "PROGLAB";
    private const int PartCount = 5;
    private const int Sha256HexLength = 64;

    public string Format() => string.Join(';', Prefix, Ip, Port, Version, Fingerprint);

    /// <summary>Ağdan gelen metne güvenilmez: biçimi tutmayan her mesaj yok sayılır.</summary>
    public static bool TryParse(string? text, out DiscoveryMessage? message)
    {
        message = null;
        var parts = text?.Split(';');
        if (parts is not { Length: PartCount } || parts[0] != Prefix)
        {
            return false;
        }

        var validIp = IPAddress.TryParse(parts[1], out _);
        var validPort = int.TryParse(parts[2], out var port) && port is > 0 and <= 65535;
        var validFingerprint = parts[4].Length == Sha256HexLength && parts[4].All(Uri.IsHexDigit);
        if (!validIp || !validPort || !validFingerprint)
        {
            return false;
        }

        message = new DiscoveryMessage(parts[1], port, parts[3], parts[4]);
        return true;
    }
}
