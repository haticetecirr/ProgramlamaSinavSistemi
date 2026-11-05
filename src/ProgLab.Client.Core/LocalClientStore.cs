using System.Text.Json;

namespace ProgLab.Client.Core;

/// <summary>
/// Bu bilgisayarda hatırlanan bağlantı bilgileri: son sunucu ve sunucu sertifika parmak izleri.
/// Gizli bilgi değildir; parola veya token buraya yazılmaz.
/// </summary>
public sealed class LocalClientStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Lock _lock = new();

    /// <summary>Kullanıcının uygulama verisi klasörü; PROGLAB_CLIENT_DATA ile değiştirilebilir (denemeler için).</summary>
    public static LocalClientStore ForApp(string appName)
    {
        var root = Environment.GetEnvironmentVariable("PROGLAB_CLIENT_DATA");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProgLab");
        }

        return new LocalClientStore(Path.Combine(root, appName, "client.json"));
    }

    public string? LastServer
    {
        get => Read().LastServer;
        set => Update(data => data.LastServer = value);
    }

    public string? GetPin(string serverKey) => Read().Pins.GetValueOrDefault(serverKey);

    public void SetPin(string serverKey, string fingerprint) => Update(data => data.Pins[serverKey] = fingerprint);

    public void RemovePin(string serverKey) => Update(data => data.Pins.Remove(serverKey));

    private ClientData Read()
    {
        lock (_lock)
        {
            if (!File.Exists(filePath))
            {
                return new ClientData();
            }

            try
            {
                return JsonSerializer.Deserialize<ClientData>(File.ReadAllText(filePath)) ?? new ClientData();
            }
            catch (JsonException)
            {
                return new ClientData(); // Bozuk dosya uygulamayı açılmaz hâle getirmesin.
            }
        }
    }

    private void Update(Action<ClientData> change)
    {
        lock (_lock)
        {
            var data = Read();
            change(data);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(data, JsonOptions));
        }
    }

    private sealed class ClientData
    {
        public string? LastServer { get; set; }

        public Dictionary<string, string> Pins { get; set; } = [];
    }
}
