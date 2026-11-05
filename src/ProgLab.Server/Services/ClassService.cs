using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProgLab.Server.Data;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Errors;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Services;

/// <summary>Şubeler ve şube üyelikleri.</summary>
public sealed class ClassService(AppDbContext db)
{
    public async Task<IReadOnlyList<ClassDto>> ListAsync(CancellationToken ct) =>
        await db.Classes.AsNoTracking()
            .OrderBy(c => c.Term).ThenBy(c => c.Name)
            .Select(c => new ClassDto(c.Id, c.Name, c.Term, c.Members.Count))
            .ToListAsync(ct);

    public async Task<ClassDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var schoolClass = await db.Classes.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new ClassDetailDto(
                c.Id,
                c.Name,
                c.Term,
                c.Members
                    .OrderBy(m => m.Student!.Username)
                    .Select(m => new UserDto(m.Student!.Id, m.Student.Username, m.Student.FullName, m.Student.Email, m.Student.Role, m.Student.IsActive, m.Student.VerifiedAt))
                    .ToList()))
            .SingleOrDefaultAsync(ct);

        return schoolClass ?? throw NotFound();
    }

    public async Task<ClassDto> CreateAsync(SaveClassRequest request, int instructorId, CancellationToken ct)
    {
        var schoolClass = new SchoolClass
        {
            Name = request.Name.Trim(),
            Term = request.Term.Trim(),
            InstructorId = instructorId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Classes.Add(schoolClass);
        await SaveWithUniqueCheckAsync(ct);

        return new ClassDto(schoolClass.Id, schoolClass.Name, schoolClass.Term, 0);
    }

    public async Task<ClassDto> UpdateAsync(int id, SaveClassRequest request, CancellationToken ct)
    {
        var schoolClass = await db.Classes.Include(c => c.Members).SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound();
        schoolClass.Name = request.Name.Trim();
        schoolClass.Term = request.Term.Trim();
        await SaveWithUniqueCheckAsync(ct);

        return new ClassDto(schoolClass.Id, schoolClass.Name, schoolClass.Term, schoolClass.Members.Count);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var schoolClass = await db.Classes.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound();

        if (await db.Exams.AnyAsync(e => e.ClassId == id, ct))
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.ClassHasExams, "İçinde sınav olan şube silinemez.");
        }

        db.Classes.Remove(schoolClass);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Üyelik listesini verilen listeyle tümüyle değiştirir; kalan üyelerin ekleme tarihi korunur.</summary>
    public async Task<ClassDetailDto> ReplaceMembersAsync(int id, IReadOnlyList<int> studentIds, CancellationToken ct)
    {
        var schoolClass = await db.Classes.Include(c => c.Members).SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound();

        var wanted = studentIds.ToHashSet();
        var validCount = await db.Users.CountAsync(u => wanted.Contains(u.Id) && u.Role == UserRole.Student, ct);
        if (validCount != wanted.Count)
        {
            throw new ApiException(StatusCodes.Status422UnprocessableEntity, ErrorCodes.InvalidStudent, "Listede öğrenci olmayan bir kullanıcı var.");
        }

        schoolClass.Members.RemoveAll(m => !wanted.Contains(m.StudentId));

        var now = DateTimeOffset.UtcNow;
        var existing = schoolClass.Members.Select(m => m.StudentId).ToHashSet();
        foreach (var studentId in wanted.Except(existing))
        {
            schoolClass.Members.Add(new ClassMember { StudentId = studentId, AddedAt = now });
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task SaveWithUniqueCheckAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.ClassNameExists, "Bu dönemde aynı adlı bir şube var.");
        }
    }

    private static ApiException NotFound() =>
        new(StatusCodes.Status404NotFound, ErrorCodes.ClassNotFound, "Şube bulunamadı.");
}
