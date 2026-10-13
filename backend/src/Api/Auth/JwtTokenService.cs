using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Monitoring.Infrastructure.Services;

namespace Monitoring.Api.Auth;

/// <summary>Выдаёт пару токенов. Access живёт недолго и ходит в Authorization. Refresh только на /api/auth/refresh, как access его не принимают.</summary>
public sealed class JwtTokenService(IConfiguration configuration)
{
    public const string AccessUse = "access";
    public const string RefreshUse = "refresh";

    /// <summary>Вход: пользователь. Выход: access, refresh и тот же пользователь.</summary>
    public AuthTokens Issue(AuthenticatedUser user)
    {
        return new AuthTokens(Create(user, AccessUse, AccessLifetime), Create(user, RefreshUse, RefreshLifetime), user);
    }

    /// <summary>Вход: refresh-строка. Выход: id и логин или null, если токен битый, протух или это access.</summary>
    public AuthenticatedUser? ReadRefresh(string token)
    {
        try
        {
            var principal = Handler.ValidateToken(token, Parameters(), out _);
            if (principal.FindFirst("token_use")?.Value != RefreshUse)
            {
                return null;
            }

            var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var login = principal.FindFirstValue(ClaimTypes.Name);
            var role = principal.FindFirstValue(ClaimTypes.Role);
            if (!Guid.TryParse(id, out var userId) || login is null || !Enum.TryParse<Monitoring.Core.UserRole>(role, out var parsed))
            {
                return null;
            }

            return new AuthenticatedUser(userId, login, parsed);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string Create(AuthenticatedUser user, string use, TimeSpan lifetime)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Login),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("token_use", use),
        };
        var jwt = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: new SigningCredentials(Key(), SecurityAlgorithms.HmacSha256));
        return Handler.WriteToken(jwt);
    }

    private TokenValidationParameters Parameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = configuration["Jwt:Issuer"],
        ValidAudience = configuration["Jwt:Audience"],
        IssuerSigningKey = Key(),
        ClockSkew = TimeSpan.FromMinutes(1),
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role,
    };

    private SymmetricSecurityKey Key() =>
        new(Encoding.UTF8.GetBytes(configuration["Jwt:Key"] ?? throw new InvalidOperationException("Задайте Jwt:Key.")));

    private TimeSpan AccessLifetime => TimeSpan.FromMinutes(configuration.GetValue("Jwt:AccessMinutes", 120));

    private TimeSpan RefreshLifetime => TimeSpan.FromDays(configuration.GetValue("Jwt:RefreshDays", 7));

    private static readonly JwtSecurityTokenHandler Handler = new();
}

public sealed record AuthTokens(string AccessToken, string RefreshToken, AuthenticatedUser User);
