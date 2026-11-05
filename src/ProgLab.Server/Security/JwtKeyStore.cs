using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProgLab.Server.Security;

/// <summary>JWT imza anahtarını ilk açılışta üretir ve Git'e girmeyen yerel ayar dosyasına yazar.</summary>
public static class JwtKeyStore
{
    private const int KeyBytes = 64;

    public static void EnsureSigningKey(ConfigurationManager configuration, string localSettingsPath)
    {
        if (!string.IsNullOrWhiteSpace(configuration[$"{JwtOptions.Section}:{nameof(JwtOptions.SigningKey)}"]))
        {
            return;
        }

        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));

        // Dosyadaki diğer ayarlara dokunmadan yalnızca anahtarı ekle.
        var root = File.Exists(localSettingsPath)
            ? JsonNode.Parse(File.ReadAllText(localSettingsPath))!.AsObject()
            : new JsonObject();
        root[JwtOptions.Section] = new JsonObject { [nameof(JwtOptions.SigningKey)] = key };
        File.WriteAllText(localSettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        configuration[$"{JwtOptions.Section}:{nameof(JwtOptions.SigningKey)}"] = key;
    }
}
