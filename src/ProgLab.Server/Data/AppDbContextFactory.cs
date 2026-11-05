using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProgLab.Server.Data;

/// <summary>
/// Yalnızca "dotnet ef migrations add" komutu için. Migration üretmek veritabanına bağlanmaz,
/// bu yüzden buradaki bağlantı dizesinde gerçek bir parola yoktur.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContext.Configure(options, "Host=localhost;Database=proglab");
        return new AppDbContext(options.Options);
    }
}
