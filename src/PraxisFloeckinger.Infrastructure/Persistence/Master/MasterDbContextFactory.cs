using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PraxisFloeckinger.Infrastructure.Persistence.Master;

/// <summary>
/// Design-Time-Factory für <see cref="MasterDbContext"/>.
/// Wird ausschließlich von <c>dotnet tool run dotnet-ef</c> verwendet —
/// nie zur Laufzeit. Produktions-Connection-Strings kommen via DI aus dem
/// Secret-Store.
/// </summary>
public sealed class MasterDbContextFactory : IDesignTimeDbContextFactory<MasterDbContext>
{
    /// <inheritdoc/>
    public MasterDbContext CreateDbContext(string[] args)
    {
        // Für dotnet-ef: ENV-Variable hat Vorrang, dann lokaler Dev-Default.
        // Der Dev-Default muss mit .env (POSTGRES_PASSWORD) und
        // appsettings.Development.json übereinstimmen.
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Master")
            ?? "Host=localhost;Port=5432;Database=tenants_master;Username=praxisdev;Password=dev_password_local";

        var options = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new MasterDbContext(options);
    }
}
