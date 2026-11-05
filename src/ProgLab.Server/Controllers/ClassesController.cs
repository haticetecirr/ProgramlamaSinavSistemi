using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProgLab.Server.Security;
using ProgLab.Server.Services;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Controllers;

[ApiController]
[Route("api/classes")]
[Authorize(Roles = nameof(UserRole.Instructor))]
public sealed class ClassesController(ClassService classes) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ClassDto>> List(CancellationToken ct) => classes.ListAsync(ct);

    [HttpGet("{id:int}")]
    public Task<ClassDetailDto> Get(int id, CancellationToken ct) => classes.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<ClassDto>> Create(SaveClassRequest request, CancellationToken ct)
    {
        var schoolClass = await classes.CreateAsync(request, User.GetUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, schoolClass);
    }

    [HttpPut("{id:int}")]
    public Task<ClassDto> Update(int id, SaveClassRequest request, CancellationToken ct) =>
        classes.UpdateAsync(id, request, ct);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await classes.DeleteAsync(id, ct);
        return Ok();
    }

    [HttpPut("{id:int}/members")]
    public Task<ClassDetailDto> ReplaceMembers(int id, UpdateMembersRequest request, CancellationToken ct) =>
        classes.ReplaceMembersAsync(id, request.StudentIds, ct);
}
