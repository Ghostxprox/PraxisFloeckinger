using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Tenancy;

namespace PraxisFloeckinger.Infrastructure.Persistence.Master;

/// <summary>
/// Idempotenter Seed für die lokale Entwicklungsumgebung.
/// Wird nur aufgerufen wenn <c>IsDevelopment() &amp;&amp; RunMigrationsOnStartup=true</c>.
/// Provisioniert beim ersten Start eine echte Tenant-DB für "floeckinger" und
/// legt Demo-Benutzer an.
/// </summary>
public static class MasterDataSeeder
{
    /// <summary>
    /// Demo-Passwort für BEIDE Dev-Accounts.
    /// DEV ONLY — niemals in Production verwenden!
    /// </summary>
    public const string DevPassword = "DevPassword123!";

    public static async Task SeedDevelopmentDataAsync(
        MasterDbContext db,
        ITenantProvisioningService provisioningService,
        ITenantDbContextFactory tenantDbContextFactory,
        TenantConnectionStringBuilder tenantConnectionStringBuilder,
        IPasswordHasher passwordHasher,
        ILogger logger)
    {
        logger.LogWarning("DEV ONLY — demo users with predictable passwords.");

        // Alten Dummy-Tenant aus Schritt 3 (DbConnectionRef "tenant_dev_*") ersetzen —
        // der hatte keine echte DB dahinter.
        var existing = await db.Tenants
            .FirstOrDefaultAsync(t => t.Subdomain == "floeckinger");

        if (existing is not null && existing.DbConnectionRef.StartsWith("tenant_dev_"))
        {
            db.Tenants.Remove(existing);
            await db.SaveChangesAsync();
            logger.LogInformation("Alter Dev-Dummy-Tenant 'floeckinger' entfernt");
            existing = null;
        }

        // Echten Tenant provisionieren wenn noch nicht vorhanden
        if (existing is null)
        {
            await provisioningService.ProvisionAsync(
                "floeckinger", "Praxis Flöckinger (Dev)", LicenseTier.Trial);
            existing = await db.Tenants.FirstAsync(t => t.Subdomain == "floeckinger");
        }

        // Demo-Benutzer in der Tenant-DB anlegen (idempotent)
        var connectionString = tenantConnectionStringBuilder.Build(existing.DbConnectionRef);
        var tenantContextStub = new TenantInfo(existing.Id, existing.Subdomain, connectionString);
        await using var tenantDb = tenantDbContextFactory.Create(tenantContextStub);

        if (await tenantDb.Users.AnyAsync())
            return;

        var pwHash = passwordHasher.Hash(DevPassword);

        var therapeut = new User
        {
            Email = "tobias@floeckinger.dev",
            PasswordHash = pwHash,
            Role = UserRole.Therapeut,
            FirstName = "Tobias",
            LastName = "Flöckinger",
        };
        var patient = new User
        {
            Email = "patient@floeckinger.dev",
            PasswordHash = pwHash,
            Role = UserRole.Patient,
            FirstName = "Max",
            LastName = "Mustermann",
        };

        tenantDb.Users.AddRange(therapeut, patient);
        await tenantDb.SaveChangesAsync();

        tenantDb.TherapistProfiles.Add(new TherapistProfile
        {
            UserId = therapeut.Id,
            Specializations = ["Verhaltenstherapie", "Stressbewältigung"],
            IsSupervisor = false,
        });
        tenantDb.PatientProfiles.Add(new PatientProfile
        {
            UserId = patient.Id,
            BirthDate = new DateOnly(1990, 5, 15),
            Address = "Musterstraße 1, 1010 Wien",
        });
        await tenantDb.SaveChangesAsync();

        logger.LogInformation(
            "Demo-Benutzer für Dev-Tenant 'floeckinger' angelegt (DB: {DbRef})",
            existing.DbConnectionRef);
    }
}
