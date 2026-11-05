using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProgLab.Server.Security;
using ProgLab.Server.Services;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Controllers;

[ApiController]
[Route("api/student/submissions")]
[Authorize(Roles = nameof(UserRole.Student))]
public sealed class StudentSubmissionsController(SubmissionService submissions) : ControllerBase
{
    [HttpPut("{submissionId:long}/answers/{examQuestionId:int}")]
    public async Task<IActionResult> SaveAnswer(long submissionId, int examQuestionId, SaveAnswerRequest request, CancellationToken ct)
    {
        await submissions.SaveAnswerAsync(submissionId, examQuestionId, request, User.GetUserId(), HttpContext.Connection.RemoteIpAddress, ct);
        return Ok();
    }

    [HttpPost("{submissionId:long}/submit")]
    public Task<SubmitResponse> Submit(long submissionId, CancellationToken ct) =>
        submissions.SubmitAsync(submissionId, User.GetUserId(), HttpContext.Connection.RemoteIpAddress, ct);
}
