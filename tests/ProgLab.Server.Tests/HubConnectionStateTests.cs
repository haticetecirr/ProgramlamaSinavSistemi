using ProgLab.Client.Core;
using ProgLab.Server.Tests.Infrastructure;
using ProgLab.Shared.Enums;

namespace ProgLab.Server.Tests;

/// <summary>
/// Ekrandaki bağlantı yazısı StateChanged olayıyla güncellenir. Sunucunun "hazır" sinyali, istemci kendini
/// "bağlı" saymadan önce gelebilir: bağlantı kurulduysa en az bir olay bunu ekrana söylemiş olmalıdır,
/// yoksa yazı "bağlantı yok"ta takılı kalır.
/// </summary>
public sealed class HubConnectionStateTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    [Fact]
    public async Task AfterStart_ScreenIsToldConnected()
    {
        await TestAccounts.InstructorAsync(fixture.Server);
        var tokens = new TokenStore();
        var api = new ApiClient(fixture.Server.CreateClient(), tokens);
        await api.LoginAsync("egitmen", TestAccounts.InstructorPassword, UserRole.Instructor);

        const int attempts = 100;
        var stuck = 0;
        for (var i = 0; i < attempts; i++)
        {
            await using var hub = TestHubConnection.Create(fixture.Server, tokens);
            var told = 0;
            hub.StateChanged += () =>
            {
                if (hub.IsConnected)
                {
                    Interlocked.Exchange(ref told, 1);
                }
            };

            await hub.StartAsync();

            Assert.True(hub.IsConnected);
            if (Volatile.Read(ref told) == 0)
            {
                stuck++;
            }
        }

        Assert.True(stuck == 0, $"{attempts} bağlantının {stuck} tanesinde ekran 'bağlantı yok'ta kaldı.");
    }
}
