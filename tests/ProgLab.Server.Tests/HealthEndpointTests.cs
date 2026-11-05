using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProgLab.Client.Core;
using ProgLab.Server.Controllers;
using ProgLab.Server.Data;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared.Dtos;

namespace ProgLab.Server.Tests;

/// <summary>Sunucuyu bellekte gerçekten başlatıp /api/health uç noktasını dener.</summary>
public sealed class HealthEndpointTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    // Bu portta PostgreSQL yok; bağlantı hemen reddedilir.
    private const string UnreachableConnectionString = "Host=localhost;Port=1;Database=proglab_test;Username=x;Password=x;Timeout=2";

    [Fact]
    public async Task Health_Returns200_AndDatabaseOk()
    {
        var client = fixture.Server.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("ok", json.RootElement.GetProperty("database").GetString());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("version").GetString()));
    }

    [Fact]
    public async Task ApiClient_ReadsHealth_ThroughSharedDto()
    {
        var api = new ApiClient(fixture.Server.CreateClient());

        var health = await api.GetHealthAsync();

        Assert.Equal("ok", health.Status);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var client = fixture.Server.CreateClient();

        var response = await client.GetAsync("/api/olmayan-adres");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_Returns503_WhenDatabaseIsLostWhileRunning()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContext.Configure(options, UnreachableConnectionString);
        await using var db = new AppDbContext(options.Options);

        var result = await new HealthController(db).Get(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, objectResult.StatusCode);
        Assert.Equal("unreachable", Assert.IsType<HealthResponse>(objectResult.Value).Database);
    }

    [Fact]
    public void Server_DoesNotStart_WhenDatabaseIsUnreachable()
    {
        using var factory = new ProgLabServerFactory(UnreachableConnectionString);

        Assert.ThrowsAny<Exception>(() => factory.Services);
    }
}
