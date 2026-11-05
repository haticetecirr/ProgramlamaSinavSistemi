using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ProgLab.Server.Data;
using ProgLab.Shared.Dtos;

namespace ProgLab.Server.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken ct)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        // Sunucu çalışırken veritabanı bağlantısı koparsa 503 döner.
        if (await db.Database.CanConnectAsync(ct))
        {
            return Ok(new HealthResponse("ok", "ok", version));
        }

        return StatusCode(StatusCodes.Status503ServiceUnavailable, new HealthResponse("unavailable", "unreachable", version));
    }
}
