using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProgLab.Server.Services;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Controllers;

[ApiController]
[Route("api/exams/{examId:int}/logs")]
[Authorize(Roles = nameof(UserRole.Instructor))]
public sealed class LogsController(LogQueryService logs) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<LogEntryDto>> List(
        int examId,
        [FromQuery(Name = "event")] string? eventType,
        [FromQuery] string? username,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct,
        [FromQuery] int page = 1) =>
        logs.ListAsync(examId, eventType, username, from, to, page, ct);

    [HttpGet("file")]
    public async Task<ContentResult> File(int examId, CancellationToken ct) =>
        Content(await logs.GetFileAsync(examId, ct), "text/plain", Encoding.UTF8);
}
