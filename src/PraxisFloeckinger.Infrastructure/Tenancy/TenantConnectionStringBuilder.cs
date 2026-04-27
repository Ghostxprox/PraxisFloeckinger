using Microsoft.Extensions.Configuration;

namespace PraxisFloeckinger.Infrastructure.Tenancy;

/// <summary>
/// Baut den Tenant-Connection-String aus einem Template-String, in dem <c>{DB}</c>
/// durch den <see cref="Core.Tenancy.Tenant.DbConnectionRef"/> ersetzt wird.
/// Den fertigen Connection-String niemals loggen — er enthält Credentials.
/// </summary>
public sealed class TenantConnectionStringBuilder
{
    private readonly string _template;

    public TenantConnectionStringBuilder(IConfiguration configuration)
    {
        _template = configuration.GetConnectionString("TenantTemplate")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:TenantTemplate ist nicht konfiguriert.");
    }

    public string Build(string dbConnectionRef) =>
        _template.Replace("{DB}", dbConnectionRef, StringComparison.Ordinal);
}
