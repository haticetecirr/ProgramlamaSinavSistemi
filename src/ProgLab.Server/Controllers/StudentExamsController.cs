using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProgLab.Server.Security;
using ProgLab.Server.Services;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Controllers;

[ApiController]
[Route("api/student/exams")]
[Authorize(Roles = nameof(UserRole.Student))]
public sealed class StudentExamsController(StudentExamService exams, SubmissionService submissions) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<StudentExamSummaryDto>> Lobby(CancellationToken ct) =>
        exams.GetLobbyAsync(User.GetUserId(), ct);

    [HttpPost("{id:int}/join")]
    public Task<JoinExamResponse> Join(int id, CancellationToken ct) =>
        submissions.JoinAsync(id, User.GetUserId(), HttpContext.Connection.RemoteIpAddress, ct);

    [HttpGet("{id:int}/paper")]
    public Task<StudentPaperDto> Paper(int id, CancellationToken ct) =>
        submissions.GetPaperAsync(id, User.GetUserId(), ct);
}
