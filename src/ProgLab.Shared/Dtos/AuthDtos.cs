using System.ComponentModel.DataAnnotations;
using ProgLab.Shared.Enums;

namespace ProgLab.Shared.Dtos;

/// <param name="App">İsteği gönderen uygulama; yanlış uygulamayla giriş reddedilir.</param>
public sealed record LoginRequest(
    [Required, MaxLength(32)] string Username,
    [Required, MaxLength(72)] string Password,
    UserRole App);

public sealed record LoginResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    UserRole Role,
    string FullName,
    bool MustChangePassword);

/// <summary>Öğrencinin ilk girişi: öğrenci no + kayıtlı e-posta eşleşirse parolasını belirler.</summary>
public sealed record FirstLoginRequest(
    [Required, MaxLength(32)] string Username,
    [Required, MaxLength(150)] string Email,
    [Required, MaxLength(72)] string NewPassword);

public sealed record ChangePasswordRequest(
    [Required, MaxLength(72)] string CurrentPassword,
    [Required, MaxLength(72)] string NewPassword);

public sealed record MeResponse(
    int Id,
    string Username,
    string FullName,
    string? Email,
    UserRole Role,
    bool MustChangePassword);
