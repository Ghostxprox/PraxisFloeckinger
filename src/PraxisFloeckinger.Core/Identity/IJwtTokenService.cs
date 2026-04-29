using PraxisFloeckinger.Core.Tenancy;

namespace PraxisFloeckinger.Core.Identity;

/// <summary>
/// Gibt signierte RS256-Access- und MFA-Session-Tokens aus.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>Lifetime des normalen Access-Tokens (aus Konfiguration).</summary>
    TimeSpan AccessTokenLifetime { get; }

    /// <summary>Lifetime des MFA-Session-Tokens (immer 5 Minuten).</summary>
    TimeSpan MfaSessionTokenLifetime { get; }

    /// <summary>
    /// Erstellt und signiert ein JWT.
    /// <para>
    /// purpose="access" (default): normaler Access-Token mit Claim <c>purpose=access</c>.<br/>
    /// purpose="mfa": kurz-lebiges MFA-Session-Token (5 min) mit Claim <c>purpose=mfa</c>.
    /// </para>
    /// Claims: sub, email, role, tenant_id, tenant_sub, purpose, jti.
    /// </summary>
    string IssueAccessToken(User user, ITenantContext tenant, string purpose = "access");
}
