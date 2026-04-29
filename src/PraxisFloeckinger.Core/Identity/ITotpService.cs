namespace PraxisFloeckinger.Core.Identity;

/// <summary>
/// TOTP RFC 6238: Secret-Generierung, QR-Code-Erzeugung und Code-Verifikation.
/// </summary>
public interface ITotpService
{
    /// <summary>Generiert ein neues zufälliges TOTP-Secret (20 Bytes, Base32-codiert).</summary>
    string GenerateSecret();

    /// <summary>
    /// Baut einen otpauth://totp/-URI für Authenticator-Apps.
    /// Format: otpauth://totp/Praxis%20Fl%C3%B6ckinger%20({subdomain}):{email}?secret={secret}&amp;issuer=...
    /// </summary>
    string BuildOtpAuthUri(string email, string subdomain, string secret);

    /// <summary>Rendert einen otpauth-URI als 256×256 PNG und gibt ihn Base64-codiert zurück.</summary>
    string GenerateQrCodePngBase64(string otpauthUri);

    /// <summary>
    /// Verifiziert einen 6-stelligen TOTP-Code gegen das Secret.
    /// windowSteps=1 akzeptiert ±30s Clock-Drift (eine Periode vor/nach).
    /// </summary>
    bool VerifyCode(string secret, string code, int windowSteps = 1);
}
