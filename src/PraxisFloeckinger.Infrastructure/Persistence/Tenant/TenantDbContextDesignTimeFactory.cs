using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PraxisFloeckinger.Core.Tenancy;

namespace PraxisFloeckinger.Infrastructure.Persistence.Tenant;

/// <summary>
/// Design-Time-Factory für <c>dotnet ef migrations add</c>.
/// Wird AUSSCHLIESSLICH für Migrations-Generierung verwendet — niemals in Production.
/// Liest den Connection-String aus ENV <c>ConnectionStrings__TenantDesignTime</c>
/// mit hardcoded Dev-Fallback (entspricht Tenancy:DesignTime:ConnectionString in appsettings.Development.json).
/// </summary>
public sealed class TenantDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__TenantDesignTime")
            ?? "Host=localhost;Port=5432;Database=tenant_design_time;Username=praxisdev;Password=dev_password_local";

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var tenantContext = new TenantInfo(Guid.Empty, "design-time", connectionString);
        return new TenantDbContext(options, tenantContext);
    }
}
