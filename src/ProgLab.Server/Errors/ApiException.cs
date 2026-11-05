namespace ProgLab.Server.Errors;

/// <summary>Servis katmanının "bu istek şu nedenle reddedildi" deme yolu. Middleware bunu ProblemDetails cevabına çevirir.</summary>
public sealed class ApiException(int statusCode, string code, string title) : Exception(title)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;
}
