using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Persistence.Master;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;

namespace PraxisFloeckinger.Infrastructure.Tenancy;

/// <summary>
/// Provisioniert neue Tenants:
/// 1. Subdomain-Eindeutigkeit prüfen
/// 2. Tenant-DB via CREATE DATABASE anlegen (server-level, kein EF Core)
/// 3. Tenant-Row in Master-DB speichern
/// 4. Tenant-Migrations auf die neue DB anwenden
///
/// ACHTUNG: Nicht idempotent. Schlägt Schritt 3 oder 4 nach Schritt 2 fehl,
/// bleibt eine verwaiste Tenant-DB ohne Master-Row übrig.
/// TODO: Recovery / Cleanup-Logik in einem späteren Schritt ergänzen.
/// </summary>
public sealed class TenantProvisioningService : ITenantProvisioningService
{
    private readonly MasterDbContext _masterDb;
    private readonly ITenantDbContextFactory _tenantDbContextFactory;
    private readonly TenantConnectionStringBuilder _connectionStringBuilder;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TenantProvisioningService> _logger;

    public TenantProvisioningService(
        MasterDbContext masterDb,
        ITenantDbContextFactory tenantDbContextFactory,
        TenantConnectionStringBuilder connectionStringBuilder,
        IConfiguration configuration,
        ILogger<TenantProvisioningService> logger)
    {
        _masterDb = masterDb;
        _tenantDbContextFactory = tenantDbContextFactory;
        _connectionStringBuilder = connectionStringBuilder;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ProvisionAsync(
        string subdomain,
        string displayName,
        LicenseTier tier,
        CancellationToken ct = default)
    {
        // 1. Subdomain-Eindeutigkeit prüfen
        var exists = await _masterDb.Tenants
            .AnyAsync(t => t.Subdomain == subdomain, ct);

        if (exists)
            throw new InvalidOperationException(
                $"Subdomain '{subdomain}' ist bereits vergeben.");

        // 2. Eindeutigen DB-Namen generieren (kein Bezug zur Subdomain, damit Subdomain
        //    später änderbar bleibt ohne DB umbenennen zu müssen)
        var dbConnectionRef = $"tenant_{Guid.NewGuid():N}";

        // 3. Tenant-DB via server-level Verbindung anlegen (CREATE DATABASE läuft
        //    außerhalb einer Transaktion — PostgreSQL-Einschränkung)
        var serverConnectionString = _configuration["Tenancy:ServerConnection"]
            ?? throw new InvalidOperationException(
                "Tenancy:ServerConnection ist nicht konfiguriert.");

        await using (var serverConn = new NpgsqlConnection(serverConnectionString))
        {
            await serverConn.OpenAsync(ct);
            await using var createCmd = serverConn.CreateCommand();
            // dbConnectionRef enthält nur [a-z0-9_] — kein SQL-Injection-Risiko;
            // Quoted Identifier als zusätzliche Absicherung
            createCmd.CommandText = $"CREATE DATABASE \"{dbConnectionRef}\"";
            await createCmd.ExecuteNonQueryAsync(ct);
        }

        _logger.LogInformation(
            "Tenant-DB '{DbRef}' für Subdomain '{Subdomain}' angelegt", dbConnectionRef, subdomain);

        // 4. Tenant-Row in Master-DB speichern
        var tenant = new Tenant
        {
            Subdomain = subdomain,
            DisplayName = displayName,
            DbConnectionRef = dbConnectionRef,
            LicenseTier = tier,
            IsActive = true,
        };

        _masterDb.Tenants.Add(tenant);
        await _masterDb.SaveChangesAsync(ct);

        // 5. Tenant-Migrations auf die neue DB anwenden
        var connectionString = _connectionStringBuilder.Build(dbConnectionRef);
        var tenantContextStub = new TenantInfo(tenant.Id, subdomain, connectionString);

        await using var tenantDb = _tenantDbContextFactory.Create(tenantContextStub);
        await tenantDb.Database.MigrateAsync(ct);

        _logger.LogInformation(
            "Tenant '{Subdomain}' (DB: {DbRef}, TenantId: {TenantId}) provisioniert",
            subdomain, dbConnectionRef, tenant.Id);
    }
}
