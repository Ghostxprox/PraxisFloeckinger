namespace PraxisFloeckinger.Api.Authentication;

/// <summary>
/// Antwort auf POST /api/v1/auth/login.
/// Status "ok": AccessToken + RefreshToken sind gesetzt.
/// Status "mfa_required": MfaSessionToken ist gesetzt, Token-Felder sind null.
/// Status "mfa_setup_required": Wie mfa_required — User muss 2FA erst einrichten.
/// </summary>
public sealed class LoginResponse
{
    public required string Status { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? MfaSessionToken { get; init; }
    public DateTimeOffset? AccessTokenExpiresAt { get; init; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; init; }
}
