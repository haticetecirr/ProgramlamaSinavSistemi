namespace ProgLab.Server.Security;

/// <summary>Parolalar yalnızca BCrypt özeti olarak saklanır (work factor 11).</summary>
public static class PasswordHasher
{
    public const int MinimumLength = 8;
    private const int WorkFactor = 11;

    // Kullanıcı yoksa da aynı süre harcansın diye: cevap süresinden hesabın varlığı anlaşılmasın.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("kullanici-yok", WorkFactor);

    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public static bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);

    public static void SpendVerificationTime(string password) => BCrypt.Net.BCrypt.Verify(password, DummyHash);
}
