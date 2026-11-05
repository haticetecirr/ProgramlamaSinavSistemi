using Microsoft.EntityFrameworkCore;
using ProgLab.Server.Data;

namespace ProgLab.Server.Tests.Infrastructure;

/// <summary>Sunucuyu bir kez başlatarak migration ve seed'i gerçekten çalıştırılmış bir şema hazırlar.</summary>
public sealed class MigratedDatabaseFixture : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public ProgLabServerFactory Server { get; private set; } = null!;

    public string Schema => _database.Schema;

    public string ConnectionString => _database.ConnectionString;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        Server = new ProgLabServerFactory(_database.ConnectionString);
        _ = Server.Services; // Sunucu burada başlar: migration + seed.
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContext.Configure(options, _database.ConnectionString);
        return new AppDbContext(options.Options);
    }

    public async Task DisposeAsync()
    {
        await Server.DisposeAsync();
        await _database.DisposeAsync();
    }
}
