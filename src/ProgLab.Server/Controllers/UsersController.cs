using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProgLab.Server.Services;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = nameof(UserRole.Instructor))]
public sealed class UsersController(UserService users) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserDto>> List(
        [FromQuery] UserRole? role,
        [FromQuery] int? classId,
        [FromQuery] string? search,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50) =>
        users.ListAsync(role, classId, search, page, pageSize, ct);

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await users.CreateStudentAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, user);
    }

    [HttpPut("{id:int}")]
    public Task<UserDto> Update(int id, UpdateUserRequest request, CancellationToken ct) =>
        users.UpdateStudentAsync(id, request, ct);

    [HttpPost("{id:int}/reset-first-login")]
    public async Task<IActionResult> ResetFirstLogin(int id, CancellationToken ct)
    {
        await users.ResetFirstLoginAsync(id, ct);
        return Ok();
    }
}
