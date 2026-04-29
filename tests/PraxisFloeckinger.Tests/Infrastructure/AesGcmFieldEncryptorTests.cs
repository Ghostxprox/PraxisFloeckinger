using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using PraxisFloeckinger.Infrastructure.Cryptography;

namespace PraxisFloeckinger.Tests.Infrastructure;

public sealed class AesGcmFieldEncryptorTests
{
    private static AesGcmFieldEncryptor CreateEncryptor(string? keyBase64 = null)
    {
        keyBase64 ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:DataKey"] = keyBase64,
            })
            .Build();
        return new AesGcmFieldEncryptor(config);
    }

    [Fact]
    public void Encrypt_Decrypt_RoundtripProducesOriginal()
    {
        var enc = CreateEncryptor();
        var plaintext = "JBSWY3DPEHPK3PXP";

        var ciphertext = enc.Encrypt(plaintext);
        var result = enc.Decrypt(ciphertext);

        result.Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_SamePlaintext_ProducesDifferentCiphertexts()
    {
        var enc = CreateEncryptor();
        var plaintext = "same-secret";

        var c1 = enc.Encrypt(plaintext);
        var c2 = enc.Encrypt(plaintext);

        c1.Should().NotBe(c2, "jede Verschlüsselung hat einen eigenen Nonce");
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsCryptographicException()
    {
        var enc = CreateEncryptor();
        var ciphertext = enc.Encrypt("secret");
        var bytes = Convert.FromBase64String(ciphertext);
        bytes[^1] ^= 0xFF; // Tag manipulieren
        var tampered = Convert.ToBase64String(bytes);

        var act = () => enc.Decrypt(tampered);
        act.Should().Throw<CryptographicException>(); // AuthenticationTagMismatchException erbt von CryptographicException
    }

    [Fact]
    public void Constructor_WrongKeyLength_Throws()
    {
        var shortKey = Convert.ToBase64String(new byte[16]); // 128 Bit statt 256
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:DataKey"] = shortKey,
            })
            .Build();

        var act = () => new AesGcmFieldEncryptor(config);
        act.Should().ThrowExactly<InvalidOperationException>()
            .WithMessage("*32 Bytes*");
    }

    [Fact]
    public void Constructor_MissingKey_Throws()
    {
        var config = new ConfigurationBuilder().Build();

        var act = () => new AesGcmFieldEncryptor(config);
        act.Should().ThrowExactly<InvalidOperationException>()
            .WithMessage("*DataKey*");
    }
}
