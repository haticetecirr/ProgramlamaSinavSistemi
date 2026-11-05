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
public sealed class ExamNotifier(IHubContext<ExamHub> hub, ConnectionTracker tracker, ILogger<ExamNotifier> logger)
{
    public const string ReasonManual = "Manual";
    public const string ReasonTimeUp = "TimeUp";

    /// <summary>Katılan öğrencinin o anki bağlantılarını sınav grubuna ekler; süre bildirimleri bu gruba gider.</summary>
    public Task AddStudentToExamAsync(int studentId, int examId) => SendSafelyAsync(async () =>
    {
        foreach (var connectionId in tracker.GetConnectionIds(studentId))
        {
            await hub.Groups.AddToGroupAsync(connectionId, ExamHub.ExamGroup(examId));
        }
    });

    public Task ExamPublishedAsync(Exam exam) => SendSafelyAsync(() =>
        hub.Clients.Group(ExamHub.ClassGroup(exam.ClassId)).SendAsync(
            HubEvents.ExamPublished,
            new ExamPublishedEvent(exam.Id, exam.Title, exam.Kind, exam.StartsAt!.Value, exam.EndsAt!.Value)));

    public Task ExamEndedAsync(Exam exam, string reason) => SendSafelyAsync(() =>
        hub.Clients.Groups(ExamHub.ClassGroup(exam.ClassId), ExamHub.InstructorsGroup).SendAsync(
            HubEvents.ExamEnded,
            new ExamEndedEvent(exam.Id, reason)));

    public Task AnswerProgressAsync(AnswerProgressEvent progress) =>
        ToInstructorsAsync(HubEvents.AnswerProgress, progress);

    public Task SubmissionReceivedAsync(SubmissionReceivedEvent submission) =>
        ToInstructorsAsync(HubEvents.SubmissionReceived, submission);

    public Task SecondDeviceAsync(SecondDeviceEvent warning) =>
        ToInstructorsAsync(HubEvents.SecondDeviceWarning, warning);

    private Task ToInstructorsAsync(string eventName, object payload) => SendSafelyAsync(() =>
        hub.Clients.Group(ExamHub.InstructorsGroup).SendAsync(eventName, payload));

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
