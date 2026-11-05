using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ProgLab.Server.Tests.Infrastructure;

/// <summary>Sunucuyu bellekte, verilen bağlantı dizesiyle başlatır. "Testing" ortamı gerçek yerel ayarları okumaz.</summary>
public sealed class ProgLabServerFactory(
    string connectionString,
    int authPermitPerMinute = 100_000,
    int timeSyncSeconds = 0,
    int examCheckSeconds = 0,
    string? examLogDirectory = null) : WebApplicationFactory<Program>
{
    public const string InstructorTemporaryPassword = "Test-Gecici-Parola-1";

    /// <summary>Bu başlıkla gelen istek o IP'den gelmiş sayılır; ikinci cihaz senaryosu için. Yalnızca testlerde vardır.</summary>
    public const string TestIpHeader = "X-Test-Ip";

    /// <summary>Yalnızca testlerde kullanılan sabit anahtar (64 bayt, Base64).</summary>
    public static readonly string SigningKey = Convert.ToBase64String(Enumerable.Range(1, 64).Select(i => (byte)i).ToArray());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Seed:InstructorTemporaryPassword", InstructorTemporaryPassword);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", authPermitPerMinute.ToString());
        builder.UseSetting("Discovery:Enabled", "false"); // Testler ağa duyuru yapmaz.
        builder.UseSetting("ExamTimer:TimeSyncSeconds", timeSyncSeconds.ToString()); // Gereken test açar.
        builder.UseSetting("ExamTimer:CheckSeconds", examCheckSeconds.ToString());
        builder.UseSetting("ExamLogs:Directory", examLogDirectory ?? "");

        builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, TestIpStartupFilter>());
    }

    private sealed class TestIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(TestIpHeader, out var ip))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
