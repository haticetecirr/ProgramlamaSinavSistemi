using ProgLab.Server.Tests.Infrastructure;

namespace ProgLab.Server.Tests;

/// <summary>İstek logu hata katmanının dışında olmalı; içinde olursa 401 gibi cevaplar logda 500 görünür.</summary>
public sealed class RequestPipelineOrderTests
{
    [Fact]
    public void RequestLogging_WrapsExceptionMiddleware()
    {
        var program = File.ReadAllText(Path.Combine(SolutionPaths.Root, "src", "ProgLab.Server", "Program.cs"));

        var logging = program.IndexOf("app.UseSerilogRequestLogging()", StringComparison.Ordinal);
        var errors = program.IndexOf("app.UseMiddleware<ApiExceptionMiddleware>()", StringComparison.Ordinal);

        Assert.True(logging >= 0 && errors >= 0, "Beklenen satırlar Program.cs içinde bulunamadı.");
        Assert.True(logging < errors, "UseSerilogRequestLogging, ApiExceptionMiddleware'den önce çağrılmalı.");
    }
}
