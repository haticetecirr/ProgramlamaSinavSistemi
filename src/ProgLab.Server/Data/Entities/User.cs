using ProgLab.Shared.Enums;

namespace ProgLab.Server.Data.Entities;

/// <summary>Eğitmen ve öğrenci hesapları.</summary>
public class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string FullName { get; set; }
    public string? Email { get; set; }
    public UserRole Role { get; set; }

    /// <summary>BCrypt özeti. Öğrenci ilk girişini yapana kadar boştur.</summary>
    public string? PasswordHash { get; set; }

    public bool MustChangePassword { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>Okul sistemi kimliği için ayrıldı; vizede boş kalır.</summary>
    public string? ExternalRef { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
