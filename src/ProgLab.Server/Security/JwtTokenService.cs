using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ProgLab.Server.Data.Entities;

namespace ProgLab.Server.Security;

/// <summary>JWT üretir: HS256, claim'ler sub (kullanıcı kimliği), role ve name.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    public const string RoleClaim = "role";
    public const string NameClaim = "name";

    public (string Token, DateTimeOffset ExpiresAt) Create(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddHours(options.Value.TokenLifetimeHours);

        var token = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [RoleClaim] = user.Role.ToString(),
                [NameClaim] = user.FullName
            },
            IssuedAt = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateKey(options.Value.SigningKey), SecurityAlgorithms.HmacSha256)
        });

        return (token, expiresAt);
    }

    public static SymmetricSecurityKey CreateKey(string base64Key) => new(Convert.FromBase64String(base64Key));
}
