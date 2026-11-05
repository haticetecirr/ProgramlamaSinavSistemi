namespace ProgLab.Client.Core;

/// <summary>Oturum token'ı. Yalnızca bellekte tutulur; uygulama kapanınca kaybolur.</summary>
public sealed class TokenStore
{
    public string? Token { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public void Set(string token, DateTimeOffset expiresAt)
    {
        Token = token;
        ExpiresAt = expiresAt;
    }

    public void Clear()
    {
        Token = null;
        ExpiresAt = null;
    }
}
