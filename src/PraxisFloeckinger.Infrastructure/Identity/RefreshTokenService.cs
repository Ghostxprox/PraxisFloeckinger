using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;

namespace PraxisFloeckinger.Infrastructure.Identity;

/// <summary>
/// <see cref="IRefreshTokenService"/>-Implementierung:
/// Raw-Token = 256 Bit Random, Base64Url-codiert.
/// TokenHash = SHA-256(rawToken), als Hex in DB gespeichert.
/// </summary>
public sealed class RefreshTokenService : IRefreshTokenService
{
    private static readonly TimeSpan PatientLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan StaffLifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan RememberMeLifetime = TimeSpan.FromDays(30);

    private readonly TenantDbContext _tenantDb;
    private readonly ILogger<RefreshTokenService> _logger;

    public RefreshTokenService(TenantDbContext tenantDb, ILogger<RefreshTokenService> logger)
    {
        _tenantDb = tenantDb;
        _logger = logger;
    }

    public async Task<(string token, RefreshToken entity)> IssueAsync(
        User user,
        bool rememberMe,
        string ip,
        string userAgent,
        CancellationToken ct = default)
    {
        if (user.Role == UserRole.SystemAdmin)
            throw new InvalidOperationException(
                "SystemAdmin darf keinen Refresh-Token via Tenant-Endpoint beziehen.");

        var rawToken = GenerateRawToken();
        var tokenHash = ComputeHash(rawToken);
        var lifetime = rememberMe
            ? RememberMeLifetime
            : user.Role == UserRole.Patient ? PatientLifetime : StaffLifetime;

        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime),
            CreatedFromIp = ip[..Math.Min(ip.Length, 64)],
            CreatedFromUserAgent = userAgent[..Math.Min(userAgent.Length, 512)],
        };

        _tenantDb.RefreshTokens.Add(entity);
        await _tenantDb.SaveChangesAsync(ct);

        return (rawToken, entity);
    }

    public async Task<RefreshToken?> ValidateAsync(string token, CancellationToken ct = default)
    {
        var tokenHash = ComputeHash(token);
        var entity = await _tenantDb.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

        if (entity is null) return null;
        if (entity.ExpiresAt < DateTimeOffset.UtcNow) return null;
        if (entity.RevokedAt is not null) return null;

        return entity;
    }

    public async Task RevokeAsync(string token, string reason, CancellationToken ct = default)
    {
        var tokenHash = ComputeHash(token);
        var entity = await _tenantDb.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

        if (entity is null)
        {
            _logger.LogWarning("RevokeAsync: Token nicht gefunden");
            return;
        }

        entity.RevokedAt = DateTimeOffset.UtcNow;
        entity.RevokedReason = reason;
        await _tenantDb.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        var tokens = await _tenantDb.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var t in tokens)
        {
            t.RevokedAt = now;
            t.RevokedReason = reason;
        }

        if (tokens.Count > 0)
            await _tenantDb.SaveChangesAsync(ct);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string GenerateRawToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32); // 256 Bit
        return Base64UrlEncode(bytes);
    }

    internal static string ComputeHash(string rawToken)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hashBytes); // 64 Zeichen Uppercase-Hex
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
