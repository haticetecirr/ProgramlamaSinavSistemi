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
public sealed class StudentExamsController(StudentExamService exams) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<StudentExamSummaryDto>> Lobby(CancellationToken ct) =>
        exams.GetLobbyAsync(User.GetUserId(), ct);
}
