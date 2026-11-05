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

    /// <summary>Süresi dolan sınavların aranma aralığı (saniye). 0 kapatır.</summary>
    public int CheckSeconds { get; set; } = 1;

    /// <summary>Kalan süre bildiriminin aralığı (saniye). 0 kapatır.</summary>
    public int TimeSyncSeconds { get; set; } = 30;
}

/// <summary>
/// Süresi dolan sınavları tamamlar ve açık sınavlara kalan süreyi bildirir.
/// Tüm durum veritabanındadır: sunucu yeniden başlarsa ilk kontrol süresi geçmiş sınavları tamamlar,
/// diğerleri kaldığı yerden sürer.
/// </summary>
public sealed class ExamTimerService(
    IServiceScopeFactory scopes,
    IHubContext<ExamHub> hub,
    IOptions<ExamTimerOptions> options,
    ILogger<ExamTimerService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        CompletionLoopAsync(stoppingToken),
        TimeSyncLoopAsync(stoppingToken));

    private async Task CompletionLoopAsync(CancellationToken ct)
    {
        if (options.Value.CheckSeconds <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.CheckSeconds));
        try
        {
            do
            {
                await CompleteExpiredExamsAsync(ct);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // Sunucu kapanıyor.
        }
    }

    private async Task TimeSyncLoopAsync(CancellationToken ct)
    {
        if (options.Value.TimeSyncSeconds <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.TimeSyncSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await SendTimeSyncAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Sunucu kapanıyor.
        }
    }

    private async Task CompleteExpiredExamsAsync(CancellationToken ct)
    {
        try
        {
            List<int> expired;
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTimeOffset.UtcNow;
                expired = await db.Exams.AsNoTracking()
                    .Where(e => e.Status == ExamStatus.Published && e.EndsAt <= now)
                    .Select(e => e.Id)
                    .ToListAsync(ct);
            }

            foreach (var examId in expired)
            {
                // Her sınav kendi kapsamında: biri hata verirse diğerleri etkilenmez.
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ExamCompletionService>()
                    .CompleteAsync(examId, ExamNotifier.ReasonTimeUp, null, null, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bir sonraki turda yeniden denenir; servis durmaz.
            logger.LogWarning(ex, "Süresi dolan sınavlar tamamlanamadı.");
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
