using System.Text;
using OtpNet;
using PraxisFloeckinger.Core.Identity;
using QRCoder;

namespace PraxisFloeckinger.Infrastructure.Identity;

/// <summary>
/// TOTP RFC 6238 via Otp.NET: SHA-1, 6 Digits, 30s Step.
/// QR-Code: 256×256 PNG via QRCoder.
/// Das Secret wird niemals geloggt.
/// </summary>
public sealed class TotpService : ITotpService
{
    private const string IssuerPrefix = "Praxis Flöckinger";

    public string GenerateSecret()
    {
        var secretBytes = new byte[20]; // 160 Bit
        System.Security.Cryptography.RandomNumberGenerator.Fill(secretBytes);
        return Base32Encoding.ToString(secretBytes);
    }

    public string BuildOtpAuthUri(string email, string subdomain, string secret)
    {
        var issuer = Uri.EscapeDataString($"{IssuerPrefix} ({subdomain})");
        var account = Uri.EscapeDataString(email);
        return $"otpauth://totp/{issuer}:{account}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period=30";
    }

    public string GenerateQrCodePngBase64(string otpauthUri)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(otpauthUri, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrData);
        var pngBytes = qrCode.GetGraphic(pixelsPerModule: 8); // ~256px bei typischen QR-Codes
        return Convert.ToBase64String(pngBytes);
    }

    public bool VerifyCode(string secret, string code, int windowSteps = 1)
    {
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
            return false;

        var secretBytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(secretBytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);

        return totp.VerifyTotp(
            totp: code,
            timeStepMatched: out _,
            window: new VerificationWindow(previous: windowSteps, future: windowSteps));
    }
}
