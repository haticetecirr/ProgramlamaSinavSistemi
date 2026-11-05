using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProgLab.Server.Data;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Errors;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Services;

/// <summary>Öğrenci hesaplarının yönetimi. Eğitmen hesabı bu servisten değiştirilmez.</summary>
public sealed class UserService(AppDbContext db)
{
    private const int MaxPageSize = 200;

    public async Task<PagedResult<UserDto>> ListAsync(UserRole? role, int? classId, string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Users.AsNoTracking();
        if (role is not null)
        {
            query = query.Where(u => u.Role == role);
        }

        if (classId is not null)
        {
            query = query.Where(u => db.ClassMembers.Any(m => m.ClassId == classId && m.StudentId == u.Id));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(u => EF.Functions.ILike(u.Username, pattern) || EF.Functions.ILike(u.FullName, pattern));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(u => u.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserDto(u.Id, u.Username, u.FullName, u.Email, u.Role, u.IsActive, u.VerifiedAt))
            .ToListAsync(ct);

        return new PagedResult<UserDto>(items, total, page, pageSize);
    }

    public async Task<UserDto> CreateStudentAsync(CreateUserRequest request, CancellationToken ct)
    {
        var user = new User
        {
            Username = request.Username.Trim(),
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            Role = UserRole.Student
        };
        db.Users.Add(user);
        await SaveWithUniqueCheckAsync(ct);

        return ToDto(user);
    }

    public async Task<UserDto> UpdateStudentAsync(int id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await FindStudentAsync(id, ct);
        user.FullName = request.FullName.Trim();
        user.Email = request.Email.Trim();
        user.IsActive = request.IsActive;
        await SaveWithUniqueCheckAsync(ct);

        return ToDto(user);
    }

    /// <summary>Parolayı siler; öğrenci ilk giriş akışını yeniden yapar.</summary>
    public async Task ResetFirstLoginAsync(int id, CancellationToken ct)
    {
        var user = await FindStudentAsync(id, ct);
        user.PasswordHash = null;
        user.VerifiedAt = null;
        await db.SaveChangesAsync(ct);
    }

    private async Task<User> FindStudentAsync(int id, CancellationToken ct) =>
        await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Student, ct)
        ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.UserNotFound, "Öğrenci bulunamadı.");

    private async Task SaveWithUniqueCheckAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_users_username" })
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.UsernameExists, "Bu öğrenci numarası zaten kayıtlı.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_users_email_lower" })
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.EmailExists, "Bu e-posta zaten kayıtlı.");
        }
    }

    private static UserDto ToDto(User u) => new(u.Id, u.Username, u.FullName, u.Email, u.Role, u.IsActive, u.VerifiedAt);
}
