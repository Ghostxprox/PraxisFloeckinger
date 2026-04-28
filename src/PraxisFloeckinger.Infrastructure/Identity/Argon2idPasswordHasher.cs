using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Logging;
using PraxisFloeckinger.Core.Identity;

namespace PraxisFloeckinger.Infrastructure.Identity;

/// <summary>
/// Argon2id-Implementierung von <see cref="IPasswordHasher"/>.
/// Parameter: m=64MB, t=3 Iterationen, p=4 Threads, 16-Byte-Salt, 32-Byte-Hash.
/// Output: PHC-Format <c>$argon2id$v=19$m=65536,t=3,p=4$&lt;base64-salt&gt;$&lt;base64-hash&gt;</c>
///
/// Niemals Klartextpasswort oder Hash-Werte loggen.
/// </summary>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    private const int MemorySize = 65536; // 64 MB in KB
    private const int Iterations = 3;
    private const int DegreeOfParallelism = 4;
    private const int SaltLength = 16;
    private const int HashLength = 32;

    private readonly ILogger<Argon2idPasswordHasher> _logger;

    public Argon2idPasswordHasher(ILogger<Argon2idPasswordHasher> logger)
    {
        _logger = logger;
    }

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = ComputeHash(password, salt, MemorySize, Iterations, DegreeOfParallelism);
        return FormatPhc(salt, hash, MemorySize, Iterations, DegreeOfParallelism);
    }

    public bool Verify(string password, string storedHash)
    {
        try
        {
            if (!TryParsePhc(storedHash, out var salt, out var expectedHash,
                    out var m, out var t, out var p))
            {
                _logger.LogWarning("Ungültiger PHC-Hash-Format bei Verifikation");
                return false;
            }

            var actualHash = ComputeHash(password, salt!, m, t, p);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler bei Passwort-Verifikation");
            return false;
        }
    }

    public bool NeedsRehash(string hash)
    {
        if (!TryParsePhc(hash, out _, out _, out var m, out var t, out var p))
            return true;

        return m < MemorySize || t < Iterations || p < DegreeOfParallelism;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static byte[] ComputeHash(string password, byte[] salt, int m, int t, int p)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        using var argon2 = new Argon2id(passwordBytes)
        {
            Salt = salt,
            MemorySize = m,
            Iterations = t,
            DegreeOfParallelism = p,
        };
        return argon2.GetBytes(HashLength);
    }

    private static string FormatPhc(byte[] salt, byte[] hash, int m, int t, int p)
    {
        // PHC-Standard: base64 ohne Padding, Standard-Alphabet (+/)
        var b64Salt = Convert.ToBase64String(salt).TrimEnd('=');
        var b64Hash = Convert.ToBase64String(hash).TrimEnd('=');
        return $"$argon2id$v=19$m={m},t={t},p={p}${b64Salt}${b64Hash}";
    }

    private static bool TryParsePhc(
        string phc,
        out byte[]? salt,
        out byte[]? hash,
        out int m,
        out int t,
        out int p)
    {
        salt = null;
        hash = null;
        m = 0;
        t = 0;
        p = 0;

        // Format: $argon2id$v=19$m=65536,t=3,p=4$<salt>$<hash>
        var parts = phc.Split('$');
        if (parts.Length < 6 || parts[0] != "" || parts[1] != "argon2id")
            return false;

        // Parse parameters from "m=65536,t=3,p=4"
        var paramParts = parts[3].Split(',');
        foreach (var param in paramParts)
        {
            var kv = param.Split('=');
            if (kv.Length != 2) return false;
            switch (kv[0])
            {
                case "m" when int.TryParse(kv[1], out var mv): m = mv; break;
                case "t" when int.TryParse(kv[1], out var tv): t = tv; break;
                case "p" when int.TryParse(kv[1], out var pv): p = pv; break;
                default: return false;
            }
        }

        if (m == 0 || t == 0 || p == 0) return false;

        // Decode base64 (add padding back)
        salt = Base64Decode(parts[4]);
        hash = Base64Decode(parts[5]);
        return salt != null && hash != null;
    }

    private static byte[]? Base64Decode(string s)
    {
        try
        {
            var padded = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            return Convert.FromBase64String(padded);
        }
        catch
        {
            return null;
        }
    }
}
