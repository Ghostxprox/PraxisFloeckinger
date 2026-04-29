using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using PraxisFloeckinger.Core.Cryptography;

namespace PraxisFloeckinger.Infrastructure.Cryptography;

/// <summary>
/// AES-256-GCM Field-Level-Encryption.
/// Output-Format: Base64(nonce[12] || ciphertext || tag[16]).
/// Der Key kommt aus IConfiguration["Encryption:DataKey"] (32 Bytes, Base64).
/// Die Datenbank sieht niemals den Klartext — auch nicht in Logs oder Backups.
/// </summary>
public sealed class AesGcmFieldEncryptor : IFieldEncryptor
{
    private readonly byte[] _key;

    public AesGcmFieldEncryptor(IConfiguration configuration)
    {
        var keyBase64 = configuration["Encryption:DataKey"]
            ?? throw new InvalidOperationException(
                "Encryption:DataKey ist nicht konfiguriert. " +
                "32 Bytes Base64-codiert in Konfiguration setzen.");

        _key = Convert.FromBase64String(keyBase64);
        if (_key.Length != 32)
            throw new InvalidOperationException(
                $"Encryption:DataKey muss genau 32 Bytes (256 Bit) lang sein, " +
                $"aber {_key.Length} Bytes wurden gefunden.");
    }

    public string Encrypt(string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize); // 12 Bytes
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 Bytes

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var combined = new byte[nonce.Length + ciphertext.Length + tag.Length];
        nonce.CopyTo(combined, 0);
        ciphertext.CopyTo(combined, nonce.Length);
        tag.CopyTo(combined, nonce.Length + ciphertext.Length);

        return Convert.ToBase64String(combined);
    }

    public string Decrypt(string ciphertext)
    {
        var combined = Convert.FromBase64String(ciphertext);
        const int nonceSize = 12;
        const int tagSize = 16;

        if (combined.Length < nonceSize + tagSize)
            throw new CryptographicException("Ungültiger Ciphertext: zu kurz.");

        var nonce = combined[..nonceSize];
        var tag = combined[^tagSize..];
        var encryptedData = combined[nonceSize..^tagSize];
        var plaintext = new byte[encryptedData.Length];

        using var aes = new AesGcm(_key, tagSize);
        aes.Decrypt(nonce, encryptedData, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}
