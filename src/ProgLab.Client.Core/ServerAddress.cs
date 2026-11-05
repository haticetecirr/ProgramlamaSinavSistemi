namespace ProgLab.Client.Core;

public static class ServerAddress
{
    public const int DefaultPort = 5000;

    /// <summary>Eğitmen uygulaması sunucuyla aynı bilgisayardadır.</summary>
    public static readonly Uri Local = new($"https://localhost:{DefaultPort}/");

    /// <summary>Kullanıcının yazdığı "192.168.1.10" veya "192.168.1.10:5000" metninden adres üretir; geçersizse null.</summary>
    public static Uri? FromInput(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text) || text.Contains('/') || text.Contains(' '))
        {
            return null;
        }

        var hostAndPort = text.Contains(':') ? text : $"{text}:{DefaultPort}";
        return Uri.TryCreate($"https://{hostAndPort}/", UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>Parmak izi kayıt anahtarı: "192.168.1.10:5000".</summary>
    public static string Key(Uri address) => $"{address.Host}:{address.Port}";
}
