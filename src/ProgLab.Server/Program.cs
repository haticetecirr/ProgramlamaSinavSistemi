using ProgLab.Server.Data;
using ProgLab.Server.Data.Seed;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Gizli ayarlar (bağlantı dizesi, geçici parola) bu dosyada durur ve Git'e girmez.
// Testler kendi ayarlarını verir; gerçek veritabanına yanlışlıkla bağlanmamaları için bu dosyayı okumazlar.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
}

// Uygulama logu: konsol + günlük dönen dosya (logs/server-<tarih>.log).
var logFilePath = Path.Combine(builder.Environment.ContentRootPath, "logs", "server-.log");
builder.Services.AddSerilog(logger => logger
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day));

builder.Services.AddDbContext<AppDbContext>((services, options) =>
    AppDbContext.Configure(options, services.GetRequiredService<IConfiguration>().GetConnectionString("Default")));

builder.Services.AddControllers();

var app = builder.Build();

// Veritabanına ulaşılamazsa sunucu açılmaz; hata açıkça loglanır.
try
{
    await DatabaseInitializer.InitializeAsync(app.Services);
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Sunucu başlatılamadı: veritabanı hazırlanamadı. PostgreSQL servisini ve appsettings.Local.json dosyasını kontrol edin.");
    await app.DisposeAsync();
    throw;
}

app.UseSerilogRequestLogging();
app.MapControllers();

app.Run();

// Entegrasyon testlerindeki WebApplicationFactory<Program> bu sınıfa erişebilsin diye.
public partial class Program;
