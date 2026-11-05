using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProgLab.Server.Data;
using ProgLab.Server.Data.Seed;
using ProgLab.Server.Errors;
using ProgLab.Server.Security;
using ProgLab.Server.Services;
using ProgLab.Shared;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Gizli ayarlar (bağlantı dizesi, geçici parola, JWT anahtarı) bu dosyada durur ve Git'e girmez.
// Testler kendi ayarlarını verir; gerçek veritabanına yanlışlıkla bağlanmamaları için bu dosyayı okumazlar.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
    JwtKeyStore.EnsureSigningKey(builder.Configuration, Path.Combine(builder.Environment.ContentRootPath, "appsettings.Local.json"));
}

// Uygulama logu: konsol + günlük dönen dosya (logs/server-<tarih>.log).
var logFilePath = Path.Combine(builder.Environment.ContentRootPath, "logs", "server-.log");
builder.Services.AddSerilog(logger => logger
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day));

builder.Services.AddDbContext<AppDbContext>((services, options) =>
    AppDbContext.Configure(options, services.GetRequiredService<IConfiguration>().GetConnectionString("Default")));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.Configure<AuthRateLimitOptions>(builder.Configuration.GetSection(AuthRateLimitOptions.Section));

builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ClassService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<QuestionService>();
builder.Services.AddScoped<ExamService>();

// JWT doğrulama. Hatalı veya süresi dolmuş token da diğer hatalarla aynı biçimde cevaplanır.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwt) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            IssuerSigningKey = JwtTokenService.CreateKey(jwt.Value.SigningKey),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = JwtTokenService.NameClaim,
            RoleClaimType = JwtTokenService.RoleClaim
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                return context.AuthenticateFailure is SecurityTokenExpiredException
                    ? ApiProblem.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.TokenExpired, "Oturum süresi doldu.")
                    : ApiProblem.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.TokenInvalid, "Oturum geçersiz.");
            },
            OnForbidden = context =>
                ApiProblem.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "Bu işlem için yetkiniz yok.")
        };
    });
builder.Services.AddAuthorization();

// Giriş uç noktalarında IP başına dakikalık deneme sınırı.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(AuthRateLimitOptions.PolicyName, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = context.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value.AuthPermitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.OnRejected = (context, _) =>
        new ValueTask(ApiProblem.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, ErrorCodes.RateLimit, "Çok fazla deneme yapıldı. Bir dakika sonra tekrar deneyin."));
});

builder.Services
    .AddControllers(options => options.Filters.Add<MustChangePasswordFilter>())
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(options =>
        options.InvalidModelStateResponseFactory = _ => new ObjectResult(
            ApiProblem.Create(StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest, "İstek biçimi hatalı veya eksik."))
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" }
        });

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

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseSerilogRequestLogging();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Entegrasyon testlerindeki WebApplicationFactory<Program> bu sınıfa erişebilsin diye.
public partial class Program;
