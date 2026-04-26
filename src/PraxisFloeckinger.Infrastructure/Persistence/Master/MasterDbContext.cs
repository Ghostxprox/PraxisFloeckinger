using Microsoft.EntityFrameworkCore;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;

namespace PraxisFloeckinger.Infrastructure.Persistence.Master;

/// <summary>
/// DbContext für die <c>tenants_master</c>-Datenbank.
/// Enthält ausschließlich plattformweite Entitäten (Tenants, Lizenzen, SystemAdmins).
/// Patientendaten befinden sich niemals in diesem Context.
/// </summary>
public sealed class MasterDbContext : DbContext
{
    /// <inheritdoc cref="MasterDbContext"/>
    public MasterDbContext(DbContextOptions<MasterDbContext> options) : base(options) { }

    /// <summary>Alle registrierten Praxen (Tenants).</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>Unveränderlicher Lizenz-Audit-Trail pro Tenant.</summary>
    public DbSet<LicenseEvent> LicenseEvents => Set<LicenseEvent>();

    /// <summary>Plattform-Administratoren (verwalten Tenants, kein Zugriff auf Patientendaten).</summary>
    public DbSet<SystemAdmin> SystemAdmins => Set<SystemAdmin>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureTenant(modelBuilder);
        ConfigureLicenseEvent(modelBuilder);
        ConfigureSystemAdmin(modelBuilder);
    }

    private static void ConfigureTenant(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Subdomain)
                  .HasMaxLength(63)
                  .IsRequired();
            entity.HasIndex(e => e.Subdomain)
                  .IsUnique()
                  .HasDatabaseName("IX_Tenants_Subdomain");

            entity.Property(e => e.DisplayName)
                  .HasMaxLength(200)
                  .IsRequired();

            entity.Property(e => e.CustomDomain)
                  .HasMaxLength(253);

            entity.Property(e => e.DbConnectionRef)
                  .HasMaxLength(200)
                  .IsRequired();

            entity.Property(e => e.LicenseTier)
                  .HasConversion<int>()
                  .IsRequired();

            entity.Property(e => e.IsActive)
                  .HasDefaultValue(true);

            entity.Property(e => e.CreatedAt)
                  .IsRequired();
        });
    }

    private static void ConfigureLicenseEvent(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LicenseEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.EventType)
                  .HasConversion<int>()
                  .IsRequired();

            entity.Property(e => e.EffectiveAt)
                  .IsRequired();

            entity.Property(e => e.Note)
                  .HasMaxLength(1000);

            entity.HasOne<Tenant>()
                  .WithMany()
                  .HasForeignKey(e => e.TenantId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.CreatedAt)
                  .IsRequired();
        });
    }

    private static void ConfigureSystemAdmin(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemAdmin>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Email)
                  .HasMaxLength(320)
                  .IsRequired();
            entity.HasIndex(e => e.Email)
                  .IsUnique()
                  .HasDatabaseName("IX_SystemAdmins_Email");

            entity.Property(e => e.FullName)
                  .HasMaxLength(200)
                  .IsRequired();

            entity.Property(e => e.PasswordHash)
                  .HasMaxLength(500)
                  .IsRequired();

            entity.Property(e => e.TotpEnabled)
                  .HasDefaultValue(false);

            entity.Property(e => e.IsDeleted)
                  .HasDefaultValue(false);

            entity.Property(e => e.CreatedAt)
                  .IsRequired();

            // Soft-Delete Global Query Filter: gelöschte SystemAdmins werden
            // standardmäßig aus allen Abfragen herausgefiltert.
            entity.HasQueryFilter(e => !e.IsDeleted);
        });
    }
}
