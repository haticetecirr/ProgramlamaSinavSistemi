using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProgLab.Server.Data;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Errors;
using ProgLab.Server.Logging;
using ProgLab.Server.Security;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Services;

/// <summary>Giriş, ilk giriş ve parola değiştirme kuralları.</summary>
public sealed class AuthService(AppDbContext db, JwtTokenService tokens)
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, IPAddress? ip, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == request.Username, ct);

        // Kullanıcı yoksa veya parola yanlışsa aynı cevap: hangi alanın yanlış olduğu söylenmez.
        if (user is null)
        {
            PasswordHasher.SpendVerificationTime(request.Password);
            await RejectLoginAsync(null, request, ip, StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials, "Kullanıcı adı veya parola yanlış.", ct);
        }
        else if (user.PasswordHash is not null && !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            await RejectLoginAsync(user, request, ip, StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials, "Kullanıcı adı veya parola yanlış.", ct);
        }

        if (user!.Role != request.App)
        {
            await RejectLoginAsync(user, request, ip, StatusCodes.Status403Forbidden, ErrorCodes.WrongAppRole, "Bu hesapla bu uygulamaya giriş yapılamaz.", ct);
        }

        if (!user.IsActive)
        {
            await RejectLoginAsync(user, request, ip, StatusCodes.Status403Forbidden, ErrorCodes.AccountDisabled, "Hesap pasif.", ct);
        }

        if (user.PasswordHash is null)
        {
            await RejectLoginAsync(user, request, ip, StatusCodes.Status409Conflict, ErrorCodes.FirstLoginRequired, "Önce ilk giriş işlemini tamamlamalısınız.", ct);
        }

        await LogAsync(ActivityEvents.Login, user.Id, ip, new { app = request.App.ToString() }, ct);
        return CreateResponse(user);
    }

    public async Task<LoginResponse> FirstLoginAsync(FirstLoginRequest request, IPAddress? ip, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == request.Username && u.Role == UserRole.Student, ct);

        // E-posta C# tarafında karşılaştırılır: veritabanının Türkçe yerel ayarında lower('I') = 'ı' olabilir.
        var identityMatches = user?.Email is not null
            && string.Equals(user.Email, request.Email.Trim(), StringComparison.OrdinalIgnoreCase);

        if (user is not null && user.PasswordHash is not null)
        {
            await RejectFirstLoginAsync(user, request.Username, ip, StatusCodes.Status409Conflict, ErrorCodes.AlreadyVerified, "Bu hesap için ilk giriş zaten yapılmış.", ct);
        }

        if (!identityMatches)
        {
            await RejectFirstLoginAsync(user, request.Username, ip, StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials, "Öğrenci numarası veya e-posta yanlış.", ct);
        }

        if (!user!.IsActive)
        {
            await RejectFirstLoginAsync(user, request.Username, ip, StatusCodes.Status403Forbidden, ErrorCodes.AccountDisabled, "Hesap pasif.", ct);
        }

        EnsurePasswordLength(request.NewPassword);

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        user.VerifiedAt = DateTimeOffset.UtcNow;
        await LogAsync(ActivityEvents.FirstLogin, user.Id, ip, new { app = UserRole.Student.ToString() }, ct);

        return CreateResponse(user);
    }

    public async Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);

        if (user.PasswordHash is null || !PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials, "Mevcut parola yanlış.");
        }

        EnsurePasswordLength(request.NewPassword);

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        user.VerifiedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<MeResponse> GetMeAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        return new MeResponse(user.Id, user.Username, user.FullName, user.Email, user.Role, user.MustChangePassword);
    }

    private LoginResponse CreateResponse(User user)
    {
        var (token, expiresAt) = tokens.Create(user);
        return new LoginResponse(token, expiresAt, user.Role, user.FullName, user.MustChangePassword);
    }

    private static void EnsurePasswordLength(string password)
    {
        if (password.Length < PasswordHasher.MinimumLength)
        {
            throw new ApiException(StatusCodes.Status422UnprocessableEntity, ErrorCodes.PasswordTooShort, "Parola en az 8 karakter olmalıdır.");
        }
    }

    private async Task RejectLoginAsync(User? user, LoginRequest request, IPAddress? ip, int status, string code, string title, CancellationToken ct)
    {
        await LogAsync(ActivityEvents.LoginFailed, user?.Id, ip, new { app = request.App.ToString(), reason = code, username = request.Username }, ct);
        throw new ApiException(status, code, title);
    }

    private async Task RejectFirstLoginAsync(User? user, string username, IPAddress? ip, int status, string code, string title, CancellationToken ct)
    {
        await LogAsync(ActivityEvents.LoginFailed, user?.Id, ip, new { app = UserRole.Student.ToString(), reason = code, username, firstLogin = true }, ct);
        throw new ApiException(status, code, title);
    }

    private async Task LogAsync(string eventType, int? userId, IPAddress? ip, object details, CancellationToken ct)
    {
        db.ActivityLogs.Add(new ActivityLog
        {
            EventType = eventType,
            UserId = userId,
            Ip = ip,
            Details = JsonSerializer.Serialize(details)
        });
        await db.SaveChangesAsync(ct);
    }
}
