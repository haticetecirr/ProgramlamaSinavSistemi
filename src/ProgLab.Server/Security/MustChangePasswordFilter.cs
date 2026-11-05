using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using ProgLab.Server.Data;
using ProgLab.Server.Errors;
using ProgLab.Shared;

namespace ProgLab.Server.Security;

/// <summary>Bu işarete sahip uç nokta, parola değiştirme zorunluluğu varken de çalışır.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowWhilePasswordChangeRequiredAttribute : Attribute;

/// <summary>
/// Her kimliği doğrulanmış istekte hesabın güncel durumunu kontrol eder. Token 4 saat geçerli olduğu için
/// pasifleştirilen, ilk girişi sıfırlanan veya parolasını değiştirmesi gereken hesap burada yakalanır.
/// </summary>
public sealed class MustChangePasswordFilter(AppDbContext db) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userId = context.HttpContext.User.GetUserId();
        var account = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.MustChangePassword, HasPassword = u.PasswordHash != null })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        if (account is null || !account.HasPassword)
        {
            throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.TokenInvalid, "Oturum geçersiz.");
        }

        if (!account.IsActive)
        {
            throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.AccountDisabled, "Hesap pasif.");
        }

        var allowed = context.ActionDescriptor.EndpointMetadata.OfType<AllowWhilePasswordChangeRequiredAttribute>().Any();
        if (account.MustChangePassword && !allowed)
        {
            throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.PasswordChangeRequired, "Önce parolanızı değiştirmelisiniz.");
        }
    }
}

public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
