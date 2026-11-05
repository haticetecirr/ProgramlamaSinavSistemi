namespace ProgLab.Client.Core;

/// <summary>Bir sunucuya açılan bağlantının parçaları: REST istemcisi, bildirim istemcisi, sertifika doğrulayıcı.</summary>
public sealed record ServerSession(Uri BaseAddress, ApiClient Api, ExamHubClient Hub, CertificatePinner? Pinner = null);

public interface IServerSessionFactory
{
    /// <param name="expectedFingerprint">UDP duyurusunda gelen parmak izi; elle girilen adreste yoktur.</param>
    ServerSession Create(Uri baseAddress, string? expectedFingerprint = null);
}

/// <summary>Gerçek ağ bağlantısı: HTTPS ve parmak izi kontrolü hem REST hem SignalR için geçerlidir.</summary>
public sealed class ServerSessionFactory(Func<Uri, string?, CertificatePinner> createPinner) : IServerSessionFactory
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Öğrenci uygulaması için.</summary>
    public static ServerSessionFactory Pinned(LocalClientStore store) =>
        new((address, expected) => CertificatePinner.Pinned(store, address, expected));

    /// <summary>Eğitmen uygulaması için (sunucu aynı bilgisayarda).</summary>
    public static ServerSessionFactory Local() => new((_, _) => CertificatePinner.LocalTrust());

    public ServerSession Create(Uri baseAddress, string? expectedFingerprint = null)
    {
        var pinner = createPinner(baseAddress, expectedFingerprint);
        var tokens = new TokenStore();

        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = ConnectTimeout,
            SslOptions = { RemoteCertificateValidationCallback = (_, certificate, _, _) => pinner.Validate(certificate) }
        };
        var http = new HttpClient(handler) { BaseAddress = baseAddress, Timeout = RequestTimeout };

        var hub = new ExamHubClient(baseAddress, tokens, options =>
        {
            options.HttpMessageHandlerFactory = inner =>
            {
                if (inner is HttpClientHandler clientHandler)
                {
                    clientHandler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) => pinner.Validate(certificate);
                }

                return inner;
            };
            options.WebSocketConfiguration = socket =>
                socket.RemoteCertificateValidationCallback = (_, certificate, _, _) => pinner.Validate(certificate);
        });

        return new ServerSession(baseAddress, new ApiClient(http, tokens), hub, pinner);
    }
}
