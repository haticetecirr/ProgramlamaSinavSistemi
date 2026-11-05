using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ProgLab.Shared.Dtos;

namespace ProgLab.Server.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<HealthResponse> Get()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        // Veritabanı Aşama 2'de bağlanır; o zaman bağlantı gerçekten denenecek ve hata varsa 503 dönecek.
        return Ok(new HealthResponse("ok", "not_configured", version));
    }
}
