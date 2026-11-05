using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProgLab.Server.Services;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize(Roles = nameof(UserRole.Instructor))]
public sealed class CategoriesController(CategoryService categories) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CategoryDto>> GetTree(CancellationToken ct) => categories.GetTreeAsync(ct);

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(SaveCategoryRequest request, CancellationToken ct)
    {
        var category = await categories.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, category);
    }

    [HttpPut("{id:int}")]
    public Task<CategoryDto> Update(int id, SaveCategoryRequest request, CancellationToken ct) =>
        categories.UpdateAsync(id, request, ct);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return Ok();
    }
}
