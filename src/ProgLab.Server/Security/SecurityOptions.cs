namespace ProgLab.Server.Security;

/// <summary>appsettings "Jwt" bölümü.</summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    /// <summary>Base64 HS256 anahtarı. İlk açılışta üretilip appsettings.Local.json'a yazılır.</summary>
    public string SigningKey { get; set; } = "";

    public double TokenLifetimeHours { get; set; } = 4;
}

/// <summary>appsettings "RateLimiting" bölümü.</summary>
public sealed class AuthRateLimitOptions
{
    public const string Section = "RateLimiting";
    public const string PolicyName = "auth";

    /// <summary>IP başına dakikada giriş denemesi.</summary>
    public int AuthPermitPerMinute { get; set; } = 10;
}
