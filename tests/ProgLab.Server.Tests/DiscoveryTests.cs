using System.Net;
using System.Net.Sockets;
using System.Text;
using ProgLab.Client.Core;
using ProgLab.Server.Background;
using ProgLab.Shared;

namespace ProgLab.Server.Tests;

/// <summary>UDP sunucu duyurusu: mesaj biçimi, yayın adresi ve gerçek soket üzerinden alma.</summary>
public sealed class DiscoveryTests
{
    private static readonly string Fingerprint = new('A', 64);

    [Fact]
    public void Message_HasDesignedFormat_AndRoundTrips()
    {
        var message = new DiscoveryMessage("192.168.1.10", 5000, "1.0.0.0", Fingerprint);

        Assert.Equal($"PROGLAB;192.168.1.10;5000;1.0.0.0;{Fingerprint}", message.Format());
        Assert.True(DiscoveryMessage.TryParse(message.Format(), out var parsed));
        Assert.Equal(message, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("merhaba")]
    [InlineData("BASKA;192.168.1.10;5000;1.0;AAAA")]
    [InlineData("PROGLAB;192.168.1.10;5000;1.0")]
    [InlineData("PROGLAB;ip-degil;5000;1.0;AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("PROGLAB;192.168.1.10;0;1.0;AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("PROGLAB;192.168.1.10;99999;1.0;AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("PROGLAB;192.168.1.10;5000;1.0;kisa")]
    [InlineData("PROGLAB;192.168.1.10;5000;1.0;ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public void MalformedMessages_AreIgnored(string? text)
    {
        Assert.False(DiscoveryMessage.TryParse(text, out var message));
        Assert.Null(message);
    }

    [Theory]
    [InlineData("192.168.1.10", "255.255.255.0", "192.168.1.255")]
    [InlineData("10.20.30.40", "255.255.0.0", "10.20.255.255")]
    [InlineData("172.2.2.139", "255.255.252.0", "172.2.3.255")]
    public void DirectedBroadcast_IsCalculatedFromMask(string ip, string mask, string expected)
    {
        Assert.Equal(IPAddress.Parse(expected), DiscoveryBroadcaster.DirectedBroadcast(IPAddress.Parse(ip), IPAddress.Parse(mask)));
    }

    [Fact]
    public async Task Listener_ReceivesRealUdpMessage_AndIgnoresJunk()
    {
        var port = FreeUdpPort();
        var found = new TaskCompletionSource<DiscoveryMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var listening = new ServerDiscovery(port).ListenAsync(m => found.TrySetResult(m), cts.Token);

        using var sender = new UdpClient();
        var target = new IPEndPoint(IPAddress.Loopback, port);
        var expected = new DiscoveryMessage("192.168.1.10", 5000, "1.0.0.0", Fingerprint);

        await sender.SendAsync(Encoding.UTF8.GetBytes("gecersiz mesaj"), target);
        await sender.SendAsync(Encoding.UTF8.GetBytes(expected.Format()), target);

        Assert.Equal(expected, await found.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TwoListeners_CanRunOnTheSameComputer()
    {
        var port = FreeUdpPort();
        using var cts = new CancellationTokenSource();

        var first = new ServerDiscovery(port).ListenAsync(_ => { }, cts.Token);
        var second = new ServerDiscovery(port).ListenAsync(_ => { }, cts.Token);
        await Task.Delay(100);

        Assert.False(first.IsFaulted);
        Assert.False(second.IsFaulted);

        await cts.CancelAsync();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }
}

public sealed class ServerAddressTests
{
    [Theory]
    [InlineData("192.168.1.10", "https://192.168.1.10:5000/")]
    [InlineData(" 192.168.1.10 ", "https://192.168.1.10:5000/")]
    [InlineData("192.168.1.10:6000", "https://192.168.1.10:6000/")]
    [InlineData("egitmen-pc", "https://egitmen-pc:5000/")]
    public void ValidInput_BecomesHttpsAddress(string input, string expected)
    {
        Assert.Equal(new Uri(expected), ServerAddress.FromInput(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://192.168.1.10")]
    [InlineData("192.168.1.10/api")]
    [InlineData("iki kelime")]
    [InlineData("192.168.1.10:port")]
    public void InvalidInput_IsRejected(string? input)
    {
        Assert.Null(ServerAddress.FromInput(input));
    }

    [Fact]
    public void Key_IncludesHostAndPort()
    {
        Assert.Equal("192.168.1.10:5000", ServerAddress.Key(new Uri("https://192.168.1.10:5000/")));
    }
}
