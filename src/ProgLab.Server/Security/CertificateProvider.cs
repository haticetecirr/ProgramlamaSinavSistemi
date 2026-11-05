using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ProgLab.Server.Security;

/// <summary>
/// Sunucunun öz imzalı HTTPS sertifikası: RSA 2048, 2 yıl. İlk açılışta üretilir ve eğitmen bilgisayarında
/// saklanır, böylece parmak izi yeniden başlatmalarda değişmez. Dosya Git'e girmez.
/// İstemci sertifikaya imzadan değil, parmak izinden güvenir (CertificatePinner).
/// </summary>
public sealed class CertificateProvider(IWebHostEnvironment environment, IConfiguration configuration)
{
    private const int KeySizeBits = 2048;
    private const int ValidityYears = 2;

    private readonly Lazy<X509Certificate2> _certificate = new(() =>
    {
        var configuredPath = configuration["Server:CertificatePath"];
        if (string.IsNullOrWhiteSpace(configuredPath) && environment.IsEnvironment("Testing"))
        {
            // Test ortamı gerçek sertifika dosyasına dokunmaz: her açılışta geçici sertifika.
            return Generate(DateTimeOffset.UtcNow);
        }

        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(environment.ContentRootPath, "certs", "server.pfx")
            : configuredPath;
        return LoadOrCreate(path, DateTimeOffset.UtcNow);
    });

    public X509Certificate2 Certificate => _certificate.Value;

    /// <summary>SHA-256 parmak izi: UDP duyurusunda gider, istemci bununla karşılaştırır.</summary>
    public string Fingerprint => GetFingerprint(Certificate);

    public static string GetFingerprint(X509Certificate2 certificate) =>
        certificate.GetCertHashString(HashAlgorithmName.SHA256);

    public static X509Certificate2 LoadOrCreate(string path, DateTimeOffset now)
    {
        if (File.Exists(path))
        {
            var existing = X509CertificateLoader.LoadPkcs12FromFile(path, null);
            if (existing.NotAfter > now.UtcDateTime.AddDays(1))
            {
                return existing;
            }

            existing.Dispose();
        }

        var created = Generate(now);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, created.Export(X509ContentType.Pkcs12));
        return created;
    }

    public static X509Certificate2 Generate(DateTimeOffset now)
    {
        using var rsa = RSA.Create(KeySizeBits);
        var request = new CertificateRequest("CN=ProgLab Sinav Sunucusu", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());

        using var selfSigned = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(ValidityYears));

        // Windows TLS katmanı özel anahtarın kapsayıcıda olmasını ister; PFX'ten yeniden yüklenir.
        // Exportable: ilk açılışta diske yazılabilmesi için gerekli.
        return X509CertificateLoader.LoadPkcs12(selfSigned.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.Exportable);
    }
}
