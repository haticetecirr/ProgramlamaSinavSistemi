using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProgLab.Server.Security;
using ProgLab.Server.Services;
using ProgLab.Shared.Dtos;

namespace ProgLab.Server.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitOptions.PolicyName)]
    public Task<LoginResponse> Login(LoginRequest request, CancellationToken ct) =>
        auth.LoginAsync(request, HttpContext.Connection.RemoteIpAddress, ct);

    [HttpPost("first-login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitOptions.PolicyName)]
    public Task<LoginResponse> FirstLogin(FirstLoginRequest request, CancellationToken ct) =>
        auth.FirstLoginAsync(request, HttpContext.Connection.RemoteIpAddress, ct);

    [HttpPost("change-password")]
    [Authorize]
    [AllowWhilePasswordChangeRequired]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(User.GetUserId(), request, ct);
        return Ok();
    }

    [HttpGet("me")]
    [Authorize]
    public Task<MeResponse> Me(CancellationToken ct) => auth.GetMeAsync(User.GetUserId(), ct);
}
