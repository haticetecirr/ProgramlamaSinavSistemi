namespace ProgLab.Client.Core;

/// <summary>Sunucunun reddettiği veya ulaşılamayan istek. Message her zaman kullanıcıya gösterilebilir Türkçe metindir.</summary>
public sealed class ApiException(string code, string message, int? statusCode = null) : Exception(message)
{
    public const string ConnectionFailed = "CONNECTION_FAILED";

    public string Code { get; } = code;

    public int? StatusCode { get; } = statusCode;
}
