using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ProgLab.Client.Core;

namespace ProgLab.Server.Tests;

/// <summary>Sunucuyu bellekte gerçekten başlatıp /api/health uç noktasını dener.</summary>
public sealed class HealthEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_Returns200_WithCamelCaseFields()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("not_configured", json.RootElement.GetProperty("database").GetString());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("version").GetString()));
    }

    [Fact]
    public async Task ApiClient_ReadsHealth_ThroughSharedDto()
    {
        var api = new ApiClient(factory.CreateClient());

        var health = await api.GetHealthAsync();

        Assert.Equal("ok", health.Status);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/olmayan-adres");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
