using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using ProgLab.Client.Core;
using ProgLab.Server.Security;
using ProgLab.Shared;
using ProgLab.Shared.Dtos;
using ProgLab.StudentApp.ViewModels;

namespace ProgLab.Server.Tests;

/// <summary>Öz imzalı sertifika ve parmak izi sabitleme; gerçek TLS bağlantısı üzerinden.</summary>
public sealed class CertificateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"proglab-test-{Guid.NewGuid():N}");

    private LocalClientStore NewStore() => new(Path.Combine(_folder, "client.json"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Generated_IsRsa2048_ValidForTwoYears_WithPrivateKey()
    {
        var now = DateTimeOffset.UtcNow;

        using var certificate = CertificateProvider.Generate(now);

        using var rsa = certificate.GetRSAPrivateKey();
        Assert.NotNull(rsa);
        Assert.Equal(2048, rsa.KeySize);
        Assert.True(certificate.HasPrivateKey);
        Assert.Equal(now.AddYears(2).UtcDateTime, certificate.NotAfter.ToUniversalTime(), TimeSpan.FromMinutes(1));
        Assert.Equal(64, CertificateProvider.GetFingerprint(certificate).Length);
    }

    [Fact]
    public void LoadOrCreate_KeepsSameCertificate_AcrossRestarts()
    {
        var path = Path.Combine(_folder, "certs", "server.pfx");
        var now = DateTimeOffset.UtcNow;

        using var first = CertificateProvider.LoadOrCreate(path, now);
        using var second = CertificateProvider.LoadOrCreate(path, now);

        Assert.True(File.Exists(path));
        Assert.Equal(CertificateProvider.GetFingerprint(first), CertificateProvider.GetFingerprint(second));
        Assert.True(second.HasPrivateKey);
    }

    [Fact]
    public void LoadOrCreate_RenewsExpiredCertificate()
    {
        var path = Path.Combine(_folder, "certs", "server.pfx");
        var now = DateTimeOffset.UtcNow;

        using var old = CertificateProvider.LoadOrCreate(path, now);
        using var renewed = CertificateProvider.LoadOrCreate(path, now.AddYears(3));

        Assert.NotEqual(CertificateProvider.GetFingerprint(old), CertificateProvider.GetFingerprint(renewed));
        Assert.True(renewed.NotAfter > now.AddYears(3).UtcDateTime);
    }

    [Fact]
    public void Pinner_TrustsFirstCertificate_ThenOnlyThatOne()
    {
        var store = NewStore();
        var address = new Uri("https://192.168.1.10:5000/");
        using var first = CertificateProvider.Generate(DateTimeOffset.UtcNow);
        using var other = CertificateProvider.Generate(DateTimeOffset.UtcNow);

        Assert.True(CertificatePinner.Pinned(store, address).Validate(first));
        Assert.Equal(CertificateProvider.GetFingerprint(first), store.GetPin("192.168.1.10:5000"));

        // Uygulama yeniden açıldı: kayıt dosyadan okunur.
        Assert.True(CertificatePinner.Pinned(NewStore(), address).Validate(first));

        var pinner = CertificatePinner.Pinned(NewStore(), address);
        Assert.False(pinner.Validate(other));
        Assert.True(pinner.Rejected);
        Assert.Equal(CertificateProvider.GetFingerprint(first), store.GetPin("192.168.1.10:5000"));

        // Başka sunucunun kaydı ayrıdır.
        Assert.True(CertificatePinner.Pinned(store, new Uri("https://192.168.1.11:5000/")).Validate(other));
    }

    [Fact]
    public void Pinner_RejectsCertificate_NotMatchingAnnouncedFingerprint_AndDoesNotPinIt()
    {
        var store = NewStore();
        var address = new Uri("https://192.168.1.10:5000/");
        using var attacker = CertificateProvider.Generate(DateTimeOffset.UtcNow);

        var pinner = CertificatePinner.Pinned(store, address, expectedFingerprint: new string('A', 64));

        Assert.False(pinner.Validate(attacker));
        Assert.Null(store.GetPin("192.168.1.10:5000"));
    }

    [Fact]
    public void Pinner_RejectsMissingCertificate()
    {
        Assert.False(CertificatePinner.Pinned(NewStore(), new Uri("https://h:1/")).Validate(null));
    }

    [Fact]
    public void LocalTrust_AcceptsAnyCertificate_AndExposesFingerprint()
    {
        var pinner = CertificatePinner.LocalTrust();
        using var certificate = CertificateProvider.Generate(DateTimeOffset.UtcNow);

        Assert.True(pinner.Validate(certificate));
        Assert.Equal(CertificateProvider.GetFingerprint(certificate), pinner.Fingerprint);
    }

    [Fact]
    public void Store_RemembersLastServer_AndSurvivesCorruptFile()
    {
        var store = NewStore();
        Assert.Null(store.LastServer);

        store.LastServer = "192.168.1.10";
        Assert.Equal("192.168.1.10", NewStore().LastServer);

        File.WriteAllText(Path.Combine(_folder, "client.json"), "bozuk {");
        Assert.Null(NewStore().LastServer);
    }

    /// <summary>VT-09'un yerel kısmı: elle IP, hatırlama ve değişen sertifikanın reddi; gerçek TLS ile.</summary>
    [Fact]
    public async Task ConnectScreen_OverRealTls_PinsCertificate_RejectsChangedOne_AndCanForget()
    {
        var port = FreeTcpPort();
        var store = NewStore();
        var connected = new List<ServerSession>();
        var screen = new ConnectViewModel(ServerSessionFactory.Pinned(store), new NoDiscovery(), store, connected.Add, a => a());

        // Sunucu kapalıyken anlaşılır hata.
        screen.ManualAddress = $"127.0.0.1:{port}";
        await screen.ConnectManualCommand.ExecuteAsync(null);
        Assert.StartsWith("Sunucuya ulaşılamadı", screen.ErrorMessage);
        Assert.False(screen.CanForgetCertificate);
        Assert.Empty(connected);

        // İlk bağlantı: parmak izi kaydedilir, adres hatırlanır.
        using var original = CertificateProvider.Generate(DateTimeOffset.UtcNow);
        await using (await TlsTestServer.StartAsync(port, original))
        {
            await screen.ConnectManualCommand.ExecuteAsync(null);
        }

        Assert.Null(screen.ErrorMessage);
        Assert.Single(connected);
        Assert.Equal(CertificateProvider.GetFingerprint(original), store.GetPin($"127.0.0.1:{port}"));
        Assert.Equal($"127.0.0.1:{port}", store.LastServer);
        Assert.Equal(CertificateProvider.GetFingerprint(original), connected[0].Pinner!.Fingerprint);

        // Sunucu farklı sertifikayla açılınca bağlantı reddedilir ve kayıt değişmez.
        using var changed = CertificateProvider.Generate(DateTimeOffset.UtcNow);
        await using var impostor = await TlsTestServer.StartAsync(port, changed);
        var second = new ConnectViewModel(ServerSessionFactory.Pinned(store), new NoDiscovery(), store, connected.Add, a => a());
        Assert.Equal($"127.0.0.1:{port}", second.ManualAddress); // Son adres formda hazır gelir.

        await second.ConnectManualCommand.ExecuteAsync(null);
        Assert.Contains("sertifikası değişmiş", second.ErrorMessage);
        Assert.True(second.CanForgetCertificate);
        Assert.Single(connected);
        Assert.Equal(CertificateProvider.GetFingerprint(original), store.GetPin($"127.0.0.1:{port}"));

        // Kullanıcı bilerek kaydı silerse yeni sertifika kabul edilir.
        second.ForgetCertificateCommand.Execute(null);
        Assert.False(second.CanForgetCertificate);
        Assert.Null(store.GetPin($"127.0.0.1:{port}"));
        await second.ConnectManualCommand.ExecuteAsync(null);
        Assert.Equal(2, connected.Count);
        Assert.Equal(CertificateProvider.GetFingerprint(changed), store.GetPin($"127.0.0.1:{port}"));
    }

    [Fact]
    public async Task ConnectScreen_SelectedServer_RejectsCertificateDifferentFromAnnouncement()
    {
        var port = FreeTcpPort();
        var store = NewStore();
        var connected = new List<ServerSession>();
        using var certificate = CertificateProvider.Generate(DateTimeOffset.UtcNow);
        await using var server = await TlsTestServer.StartAsync(port, certificate);

        var discovery = new OneShotDiscovery(new DiscoveryMessage("127.0.0.1", port, "1.0", new string('B', 64)));
        var screen = new ConnectViewModel(ServerSessionFactory.Pinned(store), discovery, store, connected.Add, a => a());
        screen.StartDiscovery();

        screen.SelectedServer = Assert.Single(screen.Servers);
        await screen.ConnectSelectedCommand.ExecuteAsync(null);

        Assert.Contains("sertifikası değişmiş", screen.ErrorMessage);
        Assert.Empty(connected);

        // Gerçek parmak izini duyuran sunucu kabul edilir.
        var goodDiscovery = new OneShotDiscovery(new DiscoveryMessage("127.0.0.1", port, "1.0", CertificateProvider.GetFingerprint(certificate)));
        var good = new ConnectViewModel(ServerSessionFactory.Pinned(store), goodDiscovery, store, connected.Add, a => a());
        good.StartDiscovery();
        good.SelectedServer = Assert.Single(good.Servers);
        await good.ConnectSelectedCommand.ExecuteAsync(null);
        Assert.Single(connected);
    }

    private static int FreeTcpPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private sealed class NoDiscovery : IServerDiscovery
    {
        public Task ListenAsync(Action<DiscoveryMessage> onFound, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class OneShotDiscovery(DiscoveryMessage message) : IServerDiscovery
    {
        public Task ListenAsync(Action<DiscoveryMessage> onFound, CancellationToken ct)
        {
            onFound(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>Verilen sertifikayla gerçek Kestrel üzerinde /api/health sunan en küçük sunucu.</summary>
    private sealed class TlsTestServer(WebApplication app) : IAsyncDisposable
    {
        public static async Task<TlsTestServer> StartAsync(int port, X509Certificate2 certificate)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel =>
                kestrel.Listen(IPAddress.Loopback, port, listen => listen.UseHttps(certificate)));
            var app = builder.Build();
            app.MapGet("/api/health", () => new HealthResponse("ok", "ok", "test"));
            await app.StartAsync();
            return new TlsTestServer(app);
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
