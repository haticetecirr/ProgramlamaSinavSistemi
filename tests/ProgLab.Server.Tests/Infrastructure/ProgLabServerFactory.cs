using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProgLab.Server.Tests.Infrastructure;

/// <summary>Sunucuyu bellekte, verilen bağlantı dizesiyle başlatır. "Testing" ortamı gerçek yerel ayarları okumaz.</summary>
public sealed class ProgLabServerFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string InstructorTemporaryPassword = "Test-Gecici-Parola-1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Seed:InstructorTemporaryPassword", InstructorTemporaryPassword);
    }
}
