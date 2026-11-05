using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProgLab.Server.Security;
using ProgLab.Server.Services;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Controllers;

[ApiController]
[Route("api/questions")]
[Authorize(Roles = nameof(UserRole.Instructor))]
public sealed class QuestionsController(QuestionService questions) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<QuestionSummaryDto>> List(
        [FromQuery] int? categoryId,
        [FromQuery] QuestionType? type,
        [FromQuery] string? search,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50) =>
        questions.ListAsync(categoryId, type, search, page, pageSize, ct);

    [HttpGet("{id:int}")]
    public Task<InstructorQuestionDto> Get(int id, CancellationToken ct) => questions.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<InstructorQuestionDto>> Create(SaveQuestionRequest request, CancellationToken ct)
    {
        var question = await questions.CreateAsync(request, User.GetUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, question);
    }

    [HttpPut("{id:int}")]
    public Task<UpdateQuestionResponse> Update(int id, SaveQuestionRequest request, CancellationToken ct) =>
        questions.UpdateAsync(id, request, User.GetUserId(), ct);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await questions.DeleteAsync(id, ct);
        return Ok();
    }
}
