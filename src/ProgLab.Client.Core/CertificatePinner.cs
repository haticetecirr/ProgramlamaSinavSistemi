using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ProgLab.Client.Core;

/// <summary>
/// Öz imzalı sertifikaya imzayla güvenilemez; güven parmak izine dayanır (ilk kullanımda güven, TOFU):
/// ilk bağlantıda parmak izi kaydedilir, sonraki bağlantılarda yalnızca aynısı kabul edilir.
/// Sunucu UDP duyurusunda parmak izi bildirdiyse sertifika onunla da eşleşmelidir.
/// </summary>
public sealed class CertificatePinner
{
    private readonly LocalClientStore? _store;
    private readonly string _serverKey;
    private readonly string? _expectedFingerprint;

    private CertificatePinner(LocalClientStore? store, string serverKey, string? expectedFingerprint)
    {
        _store = store;
        _serverKey = serverKey;
        _expectedFingerprint = expectedFingerprint;
    }

    /// <summary>Sertifikanın son görülen parmak izi.</summary>
    public string? Fingerprint { get; private set; }

    /// <summary>Son doğrulamada sertifika reddedildi mi?</summary>
    public bool Rejected { get; private set; }

    /// <summary>Öğrenci uygulaması: ağ üzerinden bağlanır, parmak izi sabitlenir.</summary>
    public static CertificatePinner Pinned(LocalClientStore store, Uri address, string? expectedFingerprint = null) =>
        new(store, ServerAddress.Key(address), expectedFingerprint);

    /// <summary>
    /// Eğitmen uygulaması: sunucu aynı bilgisayardadır, trafik ağa çıkmaz. Parmak izi yalnızca
    /// ekranda gösterilmek üzere okunur; sertifika yenilense de uygulama açılmaya devam eder.
    /// </summary>
    public static CertificatePinner LocalTrust() => new(null, "", null);

    public bool Validate(X509Certificate? certificate)
    {
        if (certificate is null)
        {
            Rejected = true;
            return false;
        }

        Fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        Rejected = !IsTrusted(Fingerprint);
        return !Rejected;
    }

    private bool IsTrusted(string fingerprint)
    {
        if (_store is null)
        {
            return true;
        }

        if (_expectedFingerprint is not null && !Same(_expectedFingerprint, fingerprint))
        {
            return false;
        }

        var pinned = _store.GetPin(_serverKey);
        if (pinned is null)
        {
            _store.SetPin(_serverKey, fingerprint);
            return true;
        }

        return Same(pinned, fingerprint);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
