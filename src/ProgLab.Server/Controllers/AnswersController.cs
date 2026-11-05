using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProgLab.Server.Grading;
using ProgLab.Server.Security;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Controllers;

[ApiController]
[Route("api/answers")]
[Authorize(Roles = nameof(UserRole.Instructor))]
public sealed class AnswersController(GradingService grading) : ControllerBase
{
    [HttpPut("{answerId:long}/score")]
    public Task<OverrideScoreResponse> OverrideScore(long answerId, OverrideScoreRequest request, CancellationToken ct) =>
        grading.OverrideScoreAsync(answerId, request, User.GetUserId(), HttpContext.Connection.RemoteIpAddress, ct);
}
