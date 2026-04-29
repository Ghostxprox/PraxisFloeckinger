using Microsoft.Extensions.Configuration;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;

namespace PraxisFloeckinger.Infrastructure.Identity;

public sealed class AccountLockoutService : IAccountLockoutService
{
    private readonly TenantDbContext _db;
    private readonly int _maxAttempts;
    private readonly int _lockoutMinutes;

    public AccountLockoutService(TenantDbContext db, IConfiguration config)
    {
        _db = db;
        _maxAttempts    = int.TryParse(config["Auth:Lockout:MaxAttempts"],    out var ma) ? ma : 5;
        _lockoutMinutes = int.TryParse(config["Auth:Lockout:LockoutMinutes"], out var lm) ? lm : 15;
    }

    public bool IsLockedOut(User user) =>
        user.LockedUntil.HasValue && user.LockedUntil.Value > DateTimeOffset.UtcNow;

    public async Task RegisterFailureAsync(User user, CancellationToken ct = default)
    {
        user.FailedLoginAttempts++;

        if (user.FailedLoginAttempts >= _maxAttempts)
            user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(_lockoutMinutes);

        await _db.SaveChangesAsync(ct);
    }

    public async Task ResetAsync(User user, CancellationToken ct = default)
    {
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        await _db.SaveChangesAsync(ct);
    }
}
