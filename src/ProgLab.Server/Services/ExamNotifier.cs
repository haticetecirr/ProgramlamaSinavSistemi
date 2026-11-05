using Microsoft.AspNetCore.SignalR;
using ProgLab.Server.Data.Entities;
using ProgLab.Server.Hubs;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;

namespace ProgLab.Server.Services;

/// <summary>
/// Servis katmanının SignalR çıkışı. Bildirim veritabanı işlemi bittikten sonra gönderilir ve
/// hatası işlemi geri almaz: bağlı olmayan öğrenci sınavı girişte veya lobi yenilenince zaten görür.
/// </summary>
public sealed class ExamNotifier(IHubContext<ExamHub> hub, ILogger<ExamNotifier> logger)
{
    public const string ReasonManual = "Manual";

    public Task ExamPublishedAsync(Exam exam) => SendSafelyAsync(() =>
        hub.Clients.Group(ExamHub.ClassGroup(exam.ClassId)).SendAsync(
            HubEvents.ExamPublished,
            new ExamPublishedEvent(exam.Id, exam.Title, exam.Kind, exam.StartsAt!.Value, exam.EndsAt!.Value)));

    public Task ExamEndedAsync(Exam exam, string reason) => SendSafelyAsync(() =>
        hub.Clients.Groups(ExamHub.ClassGroup(exam.ClassId), ExamHub.InstructorsGroup).SendAsync(
            HubEvents.ExamEnded,
            new ExamEndedEvent(exam.Id, reason)));

    private async Task SendSafelyAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SignalR bildirimi gönderilemedi.");
        }
    }
}
