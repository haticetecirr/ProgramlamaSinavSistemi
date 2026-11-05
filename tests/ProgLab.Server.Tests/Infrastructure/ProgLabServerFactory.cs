using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProgLab.Server.Tests.Infrastructure;

/// <summary>Sunucuyu bellekte, verilen bağlantı dizesiyle başlatır. "Testing" ortamı gerçek yerel ayarları okumaz.</summary>
public sealed class ProgLabServerFactory(string connectionString, int authPermitPerMinute = 100_000) : WebApplicationFactory<Program>
{
    public const string InstructorTemporaryPassword = "Test-Gecici-Parola-1";

    /// <summary>Yalnızca testlerde kullanılan sabit anahtar (64 bayt, Base64).</summary>
    public static readonly string SigningKey = Convert.ToBase64String(Enumerable.Range(1, 64).Select(i => (byte)i).ToArray());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Seed:InstructorTemporaryPassword", InstructorTemporaryPassword);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", authPermitPerMinute.ToString());
    }
}
