namespace PraxisFloeckinger.Core.Identity;

/// <summary>
/// Argon2id-basiertes Passwort-Hashing mit PHC-Ausgabeformat.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hasht ein Klartextpasswort und gibt einen PHC-formatierten String zurück
    /// der Salt und Parameter einschließt.
    /// Format: <c>$argon2id$v=19$m=65536,t=3,p=4$&lt;base64-salt&gt;$&lt;base64-hash&gt;</c>
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Verifiziert ein Klartextpasswort gegen einen gespeicherten PHC-Hash.
    /// Verwendet CryptographicOperations.FixedTimeEquals gegen Timing-Angriffe.
    /// </summary>
    bool Verify(string password, string hash);

    /// <summary>
    /// Gibt <c>true</c> zurück wenn der Hash mit veralteten Parametern erstellt wurde
    /// (m, t oder p unterhalb der aktuellen Konstanten) und neu gehasht werden sollte.
    /// </summary>
    bool NeedsRehash(string hash);
}
