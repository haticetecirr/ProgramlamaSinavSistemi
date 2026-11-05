using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Gizli ayarlar (bağlantı dizesi, geçici parola) bu dosyada durur ve Git'e girmez.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);

// Uygulama logu: konsol + günlük dönen dosya (logs/server-<tarih>.log).
var logFilePath = Path.Combine(builder.Environment.ContentRootPath, "logs", "server-.log");
builder.Services.AddSerilog(logger => logger
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day));

builder.Services.AddControllers();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.MapControllers();

app.Run();

// Entegrasyon testlerindeki WebApplicationFactory<Program> bu sınıfa erişebilsin diye.
public partial class Program;
