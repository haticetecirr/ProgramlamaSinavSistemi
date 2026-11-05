using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProgLab.Server.Services;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Controllers;

[ApiController]
[Route("api")]
[Authorize(Roles = nameof(UserRole.Instructor))]
public sealed class ResultsController(ResultService results) : ControllerBase
{
    [HttpGet("exams/{examId:int}/results")]
    public Task<ExamResultsDto> Results(int examId, CancellationToken ct) => results.GetResultsAsync(examId, ct);

    [HttpGet("submissions/{submissionId:long}")]
    public Task<SubmissionDetailDto> Submission(long submissionId, CancellationToken ct) => results.GetSubmissionAsync(submissionId, ct);
}
