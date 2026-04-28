using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;

namespace PraxisFloeckinger.Infrastructure.Identity;

/// <summary>
/// Gibt RS256-signierte Access-JWTs aus.
/// Lifetime und Issuer/Audience kommen aus Konfiguration.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtSigningKeyProvider _keyProvider;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtTokenService(JwtSigningKeyProvider keyProvider, IConfiguration configuration)
    {
        _keyProvider = keyProvider;
        _issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer ist nicht konfiguriert.");
        _audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience ist nicht konfiguriert.");
        var minutes = int.TryParse(configuration["Jwt:AccessTokenMinutes"], out var m) ? m : 15;
        AccessTokenLifetime = TimeSpan.FromMinutes(minutes);
    }

    public TimeSpan AccessTokenLifetime { get; }

    public string IssueAccessToken(User user, ITenantContext tenant)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("role", user.Role.ToString()),
            new Claim("tenant_id", tenant.TenantId.ToString()),
            new Claim("tenant_sub", tenant.Subdomain),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var credentials = new SigningCredentials(
            _keyProvider.SigningKey,
            SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.Add(AccessTokenLifetime).UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
