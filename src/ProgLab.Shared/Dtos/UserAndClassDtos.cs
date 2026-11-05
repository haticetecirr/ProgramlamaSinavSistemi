using System.ComponentModel.DataAnnotations;
using ProgLab.Shared.Enums;

namespace ProgLab.Shared.Dtos;

/// <summary>Sayfalı liste cevabı.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record UserDto(
    int Id,
    string Username,
    string FullName,
    string? Email,
    UserRole Role,
    bool IsActive,
    DateTimeOffset? VerifiedAt);

/// <summary>Yeni öğrenci hesabı. Parola yoktur; öğrenci ilk girişte belirler.</summary>
public sealed record CreateUserRequest(
    [Required, MaxLength(32)] string Username,
    [Required, MaxLength(100)] string FullName,
    [Required, MaxLength(150), EmailAddress] string Email);

public sealed record UpdateUserRequest(
    [Required, MaxLength(100)] string FullName,
    [Required, MaxLength(150), EmailAddress] string Email,
    bool IsActive);

public sealed record ClassDto(int Id, string Name, string Term, int MemberCount);

public sealed record ClassDetailDto(int Id, string Name, string Term, IReadOnlyList<UserDto> Members);

public sealed record SaveClassRequest(
    [Required, MaxLength(100)] string Name,
    [Required, MaxLength(20)] string Term);

public sealed record UpdateMembersRequest([Required] IReadOnlyList<int> StudentIds);
