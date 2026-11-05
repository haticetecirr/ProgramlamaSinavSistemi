using Microsoft.EntityFrameworkCore;

namespace ProgLab.Server.Data.Seed;

/// <summary>Sunucu açılışında şemayı günceller, ardından eksikse demo verisini ekler.</summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Default")))
        {
            throw new InvalidOperationException("appsettings.Local.json içinde 'ConnectionStrings:Default' yok.");
        }

        await db.Database.MigrateAsync(ct);

        // Seed tek işlem: yarıda kalırsa sonraki açılışta yarım veriyle karşılaşılmaz.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await DemoDataSeeder.SeedAsync(db, configuration["Seed:InstructorTemporaryPassword"], ct);
        await transaction.CommitAsync(ct);
    }
}
