using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProgLab.Server.Data;
using ProgLab.Server.Hubs;
using ProgLab.Server.Services;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Background;

/// <summary>appsettings "ExamTimer" bölümü.</summary>
public sealed class ExamTimerOptions
{
    public const string Section = "ExamTimer";

    /// <summary>Kalan süre bildiriminin aralığı (saniye). 0 kapatır.</summary>
    public int TimeSyncSeconds { get; set; } = 30;
}

/// <summary>
/// Açık sınavlara kalan süreyi bildirir (TimeSync). İstemcideki sayaç bununla düzeltilir.
/// Süresi dolan sınavın tamamlanması ve otomatik teslim Aşama 8'de bu servise eklenir.
/// </summary>
public sealed class ExamTimerService(
    IServiceScopeFactory scopes,
    IHubContext<ExamHub> hub,
    IOptions<ExamTimerOptions> options,
    ILogger<ExamTimerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.TimeSyncSeconds <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.TimeSyncSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SendTimeSyncAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Sunucu kapanıyor.
        }
    }

    private async Task SendTimeSyncAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;

            var openExams = await db.Exams.AsNoTracking()
                .Where(e => e.Status == ExamStatus.Published && e.StartsAt <= now && now < e.EndsAt)
                .Select(e => new { e.Id, EndsAt = e.EndsAt!.Value })
                .ToListAsync(ct);

            foreach (var exam in openExams)
            {
                await hub.Clients.Group(ExamHub.ExamGroup(exam.Id)).SendAsync(
                    HubEvents.TimeSync,
                    new TimeSyncEvent(exam.Id, ExamRules.RemainingSeconds(exam.EndsAt, now)),
                    ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bir tur kaçarsa istemci kendi sayacıyla devam eder; servis durmaz.
            logger.LogWarning(ex, "Süre bildirimi gönderilemedi.");
        }
    }
}
