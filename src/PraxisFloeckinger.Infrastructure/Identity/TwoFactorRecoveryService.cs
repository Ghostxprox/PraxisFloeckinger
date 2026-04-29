using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;

namespace PraxisFloeckinger.Infrastructure.Identity;

/// <summary>
/// Recovery-Code-Format: "ABCDE-FGHJK" — 10 Zeichen aus lesbarem Alphabet,
/// aufgeteilt in zwei 5er-Gruppen mit Bindestrich.
/// Gespeichert als Argon2id-Hash (wie Passwörter).
/// </summary>
public sealed class TwoFactorRecoveryService : ITwoFactorRecoveryService
{
    // Alphabet ohne 0/O/1/I/L für Lesbarkeit
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 10;

    private readonly TenantDbContext _tenantDb;
    private readonly IPasswordHasher _hasher;

    public TwoFactorRecoveryService(TenantDbContext tenantDb, IPasswordHasher hasher)
    {
        _tenantDb = tenantDb;
        _hasher = hasher;
    }

    public async Task<IReadOnlyList<string>> GenerateAsync(
        Guid userId,
        int count = 10,
        CancellationToken ct = default)
    {
        // Alle alten Codes soft-deleten
        var old = await _tenantDb.TwoFactorRecoveryCodes
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);

        foreach (var c in old)
            c.IsDeleted = true;

        var plaintextCodes = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            var raw = GenerateRawCode();
            plaintextCodes.Add(FormatCode(raw));
            _tenantDb.TwoFactorRecoveryCodes.Add(new TwoFactorRecoveryCode
            {
                UserId = userId,
                CodeHash = _hasher.Hash(NormalizeCode(raw)),
            });
        }

        await _tenantDb.SaveChangesAsync(ct);
        return plaintextCodes;
    }

    public async Task<bool> ConsumeAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var normalized = NormalizeCode(code);
        var active = await _tenantDb.TwoFactorRecoveryCodes
            .Where(c => c.UserId == userId && c.UsedAt == null)
            .ToListAsync(ct);

        foreach (var entity in active)
        {
            if (!_hasher.Verify(normalized, entity.CodeHash))
                continue;

            entity.UsedAt = DateTimeOffset.UtcNow;
            await _tenantDb.SaveChangesAsync(ct);
            return true;
        }

        return false;
    }

    public async Task<int> CountRemainingAsync(Guid userId, CancellationToken ct = default)
        => await _tenantDb.TwoFactorRecoveryCodes
            .CountAsync(c => c.UserId == userId && c.UsedAt == null, ct);

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string GenerateRawCode()
    {
        var sb = new StringBuilder(CodeLength);
        var bytes = RandomNumberGenerator.GetBytes(CodeLength);
        foreach (var b in bytes)
            sb.Append(Alphabet[b % Alphabet.Length]);
        return sb.ToString();
    }

    private static string FormatCode(string raw)
        => $"{raw[..5]}-{raw[5..]}";

    private static string NormalizeCode(string code)
        => code.Replace("-", "").ToUpperInvariant();
}
