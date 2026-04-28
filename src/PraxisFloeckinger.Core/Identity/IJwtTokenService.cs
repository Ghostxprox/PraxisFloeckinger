using PraxisFloeckinger.Core.Tenancy;

namespace PraxisFloeckinger.Core.Identity;

/// <summary>
/// Gibt signierte RS256-Access-Tokens aus.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>Lifetime des Access-Tokens (aus Konfiguration).</summary>
    TimeSpan AccessTokenLifetime { get; }

    /// <summary>
    /// Erstellt und signiert ein JWT für den angegebenen User im aktuellen Tenant.
    /// Claims: sub, email, role, tenant_id, tenant_sub, jti.
    /// </summary>
    string IssueAccessToken(User user, ITenantContext tenant);
}
