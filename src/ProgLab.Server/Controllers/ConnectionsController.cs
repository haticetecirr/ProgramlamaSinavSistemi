using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProgLab.Server.Hubs;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Controllers;

/// <summary>Eğitmen ekranı açıldığında o an bağlı öğrenciler; sonraki değişiklikler SignalR ile gelir.</summary>
[ApiController]
[Route("api/connections")]
[Authorize(Roles = nameof(UserRole.Instructor))]
public sealed class ConnectionsController(ConnectionTracker tracker) : ControllerBase
{
    [HttpGet("students")]
    public IReadOnlyList<StudentConnectionDto> Students() => tracker.GetStudents();
}
