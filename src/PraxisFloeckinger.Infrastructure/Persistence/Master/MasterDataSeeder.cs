using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PraxisFloeckinger.Core.Tenancy;

namespace PraxisFloeckinger.Infrastructure.Persistence.Master;

/// <summary>
/// Idempotenter Seed für die Master-DB in der lokalen Entwicklungsumgebung.
/// Wird nur aufgerufen wenn <c>IsDevelopment()</c> — niemals in Production.
/// </summary>
public static class MasterDataSeeder
{
    public static async Task SeedDevelopmentDataAsync(MasterDbContext db, ILogger logger)
    {
        if (await db.Tenants.AnyAsync())
            return;

        var tenant = new Tenant
        {
            Subdomain = "floeckinger",
            DisplayName = "Praxis Flöckinger (Dev)",
            DbConnectionRef = "tenant_dev_floeckinger",
            LicenseTier = LicenseTier.Trial,
            IsActive = true,
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Dev-Tenant 'floeckinger' angelegt (TenantId: {TenantId})", tenant.Id);
    }
}
