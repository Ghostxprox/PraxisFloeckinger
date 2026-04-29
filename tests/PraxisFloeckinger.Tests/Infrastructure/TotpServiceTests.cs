using AwesomeAssertions;
using OtpNet;
using PraxisFloeckinger.Infrastructure.Identity;

namespace PraxisFloeckinger.Tests.Infrastructure;

public sealed class TotpServiceTests
{
    private readonly TotpService _sut = new();

    [Fact]
    public void GenerateSecret_ReturnValidBase32_20Bytes()
    {
        var secret = _sut.GenerateSecret();

        secret.Should().NotBeNullOrEmpty();
        // Base32 für 20 Bytes → 32 Zeichen (ohne Padding)
        var bytes = Base32Encoding.ToBytes(secret);
        bytes.Should().HaveCount(20);
    }

    [Fact]
    public void BuildOtpAuthUri_ContainsAllParts()
    {
        var uri = _sut.BuildOtpAuthUri("test@example.com", "floeckinger", "JBSWY3DPEHPK3PXP");

        uri.Should().StartWith("otpauth://totp/");
        uri.Should().Contain("JBSWY3DPEHPK3PXP");
        uri.Should().Contain("floeckinger");
        uri.Should().Contain("SHA1");
    }

    [Fact]
    public void GenerateQrCodePngBase64_ReturnsNonEmptyBase64()
    {
        var uri = _sut.BuildOtpAuthUri("test@example.com", "floeckinger", "JBSWY3DPEHPK3PXP");
        var qr = _sut.GenerateQrCodePngBase64(uri);

        qr.Should().NotBeNullOrEmpty();
        var bytes = Convert.FromBase64String(qr);
        bytes.Should().HaveCountGreaterThan(100); // PNG ist nie leer
    }

    [Fact]
    public void VerifyCode_ValidCurrentCode_ReturnsTrue()
    {
        var secret = _sut.GenerateSecret();
        // aktuellen Code via Otp.NET generieren
        var secretBytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(secretBytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        var code = totp.ComputeTotp();

        var result = _sut.VerifyCode(secret, code);

        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyCode_CodeFrom60sAgo_ReturnsFalseWithWindow1()
    {
        var secret = _sut.GenerateSecret();
        var secretBytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(secretBytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        // Zeitstempel 65 Sekunden in der Vergangenheit
        var oldTime = DateTime.UtcNow.AddSeconds(-65);
        var code = totp.ComputeTotp(oldTime);

        var result = _sut.VerifyCode(secret, code, windowSteps: 1);

        result.Should().BeFalse();
    }
}
