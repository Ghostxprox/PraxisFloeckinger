namespace PraxisFloeckinger.Core.Cryptography;

/// <summary>
/// App-Layer-Verschlüsselung für sensible Felder (z.B. TOTP-Secrets).
/// Die Datenbank sieht niemals den Klartext.
/// </summary>
public interface IFieldEncryptor
{
    /// <summary>Verschlüsselt einen Klartext-String; gibt Base64-codierten Ciphertext zurück.</summary>
    string Encrypt(string plaintext);

    /// <summary>Entschlüsselt einen Base64-codierten Ciphertext; gibt den Klartext zurück.</summary>
    string Decrypt(string ciphertext);
}
