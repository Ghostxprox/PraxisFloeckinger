using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PraxisFloeckinger.Infrastructure.Identity;

namespace PraxisFloeckinger.Tests.Infrastructure;

public sealed class Argon2idPasswordHasherTests
{
    private static Argon2idPasswordHasher CreateHasher()
        => new(NullLogger<Argon2idPasswordHasher>.Instance);

    [Fact]
    public void Hash_ValidPassword_ProducesPHCFormatString()
    {
        var hash = CreateHasher().Hash("MyPassword123!");

        hash.Should().StartWith("$argon2id$v=19$m=65536,t=3,p=4$");
        hash.Split('$').Should().HaveCount(6);
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes()
    {
        var hasher = CreateHasher();
        var hash1 = hasher.Hash("SamePassword!");
        var hash2 = hasher.Hash("SamePassword!");

        hash1.Should().NotBe(hash2, "jeder Hash muss einen frischen Salt haben");
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("CorrectPassword!");

        hasher.Verify("CorrectPassword!", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("CorrectPassword!");

        hasher.Verify("WrongPassword!", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_TamperedHash_ReturnsFalse()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("SomePassword!");

        // Hash-Wert verfälschen (letztes Zeichen ändern)
        var tampered = hash[..^1] + (hash[^1] == 'A' ? 'B' : 'A');

        hasher.Verify("SomePassword!", tampered).Should().BeFalse();
    }

    [Fact]
    public void NeedsRehash_CurrentParameters_ReturnsFalse()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("AnyPassword!");

        hasher.NeedsRehash(hash).Should().BeFalse();
    }

    [Fact]
    public void NeedsRehash_OldParameters_ReturnsTrue()
    {
        // Synthetisch einen Hash mit schwächeren Parametern bauen
        var oldHash = "$argon2id$v=19$m=32768,t=1,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        CreateHasher().NeedsRehash(oldHash).Should().BeTrue();
    }

    [Fact]
    public void Verify_InvalidPHCFormat_ReturnsFalse()
    {
        CreateHasher().Verify("password", "not-a-phc-hash").Should().BeFalse();
    }
}
