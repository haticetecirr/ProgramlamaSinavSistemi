using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProgLab.Server.Data;
using ProgLab.Server.Security;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Hubs;

/// <summary>
/// Yalnızca sunucudan istemciye bildirim iter; istemciden iş komutu gelmez (komutlar REST'tedir).
/// Bağlantı kurulurken JWT rolüne göre gruplara ekler: eğitmen "instructors", öğrenci her şubesi için "class-{id}".
/// </summary>
[Authorize]
public sealed class ExamHub(AppDbContext db, ConnectionTracker tracker) : Hub
{
    public const string InstructorsGroup = "instructors";

    public static string ClassGroup(int classId) => $"class-{classId}";

    public static string ExamGroup(int examId) => $"exam-{examId}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User!.GetUserId();
        var ct = Context.ConnectionAborted;

        // Token 4 saat geçerlidir; hesabın güncel durumu burada da kontrol edilir (REST'teki filtreyle aynı kural).
        var account = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Role, u.FullName, u.IsActive, u.MustChangePassword, HasPassword = u.PasswordHash != null })
            .SingleOrDefaultAsync(ct);
        if (account is not { IsActive: true, MustChangePassword: false, HasPassword: true })
        {
            Context.Abort();
            return;
        }

        if (account.Role == UserRole.Instructor)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, InstructorsGroup, ct);
        }
        else
        {
            await AddStudentAsync(userId, account.FullName, ct);
        }

        await base.OnConnectedAsync();

        // Gruplara ekleme bitti: bu andan sonraki bildirimler istemciye ulaşır.
        await Clients.Caller.SendAsync(HubEvents.Ready, ct);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (tracker.Remove(Context.ConnectionId) is { } student)
        {
            await Clients.Group(InstructorsGroup).SendAsync(HubEvents.StudentDisconnected, student);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task AddStudentAsync(int studentId, string fullName, CancellationToken ct)
    {
        var classIds = await db.ClassMembers.AsNoTracking()
            .Where(m => m.StudentId == studentId)
            .Select(m => m.ClassId)
            .ToListAsync(ct);
        foreach (var classId in classIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ClassGroup(classId), ct);
        }

        // Yeniden bağlanan öğrenci, sürmekte olan sınavının süre bildirimlerini almaya devam eder.
        var runningExamIds = await db.Submissions.AsNoTracking()
            .Where(s => s.StudentId == studentId && s.Exam!.Status == ExamStatus.Published)
            .Select(s => s.ExamId)
            .ToListAsync(ct);
        foreach (var examId in runningExamIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ExamGroup(examId), ct);
        }

        var ip = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
        var student = new StudentConnectionDto(studentId, fullName, ip);
        if (tracker.Add(Context.ConnectionId, student))
        {
            await Clients.Group(InstructorsGroup).SendAsync(HubEvents.StudentConnected, student, ct);
        }
    }
}
