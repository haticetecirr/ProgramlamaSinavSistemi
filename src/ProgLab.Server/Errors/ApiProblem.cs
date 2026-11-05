using Microsoft.AspNetCore.Mvc;

namespace ProgLab.Server.Errors;

/// <summary>Tüm hata cevaplarının tek biçimi: RFC 7807 ProblemDetails + "code" alanı.</summary>
public static class ApiProblem
{
    public static ProblemDetails Create(int status, string code, string title) => new()
    {
        Status = status,
        Title = title,
        Extensions = { ["code"] = code }
    };

    public static async Task WriteAsync(HttpContext context, int status, string code, string title)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(Create(status, code, title), options: null, contentType: "application/problem+json");
    }
}
