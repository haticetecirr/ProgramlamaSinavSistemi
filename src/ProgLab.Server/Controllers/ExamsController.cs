using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProgLab.Server.Security;
using ProgLab.Server.Services;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Controllers;

[ApiController]
[Route("api/exams")]
[Authorize(Roles = nameof(UserRole.Instructor))]
public sealed class ExamsController(ExamService exams) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ExamSummaryDto>> List(
        [FromQuery] ExamStatus? status,
        [FromQuery] int? classId,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50) =>
        exams.ListAsync(status, classId, page, pageSize, ct);

    [HttpPost]
    public async Task<ActionResult<InstructorExamDto>> Create(SaveExamRequest request, CancellationToken ct)
    {
        var exam = await exams.CreateAsync(request, User.GetUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, exam);
    }

    [HttpGet("{id:int}")]
    public Task<InstructorExamDto> Get(int id, CancellationToken ct) => exams.GetAsync(id, ct);

    [HttpPut("{id:int}")]
    public Task<InstructorExamDto> Update(int id, SaveExamRequest request, CancellationToken ct) =>
        exams.UpdateAsync(id, request, ct);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await exams.DeleteAsync(id, ct);
        return Ok();
    }

    [HttpPut("{id:int}/questions")]
    public Task<InstructorExamDto> ReplaceQuestions(int id, IReadOnlyList<ExamQuestionInput> questions, CancellationToken ct) =>
        exams.ReplaceQuestionsAsync(id, questions, ct);

    [HttpPost("{id:int}/publish")]
    public Task<InstructorExamDto> Publish(int id, CancellationToken ct) =>
        exams.PublishAsync(id, User.GetUserId(), HttpContext.Connection.RemoteIpAddress, ct);

    [HttpPost("{id:int}/end")]
    public Task<InstructorExamDto> End(int id, CancellationToken ct) =>
        exams.EndAsync(id, User.GetUserId(), HttpContext.Connection.RemoteIpAddress, ct);

    [HttpGet("{id:int}/live")]
    public Task<LiveExamDto> Live(int id, CancellationToken ct) => exams.GetLiveAsync(id, ct);

    [HttpPost("{id:int}/copy")]
    public async Task<ActionResult<InstructorExamDto>> Copy(int id, CancellationToken ct)
    {
        var exam = await exams.CopyAsync(id, User.GetUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, exam);
    }
}
