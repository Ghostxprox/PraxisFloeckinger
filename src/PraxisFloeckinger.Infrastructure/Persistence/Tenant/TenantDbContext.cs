using Microsoft.EntityFrameworkCore;
using PraxisFloeckinger.Core.Common;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;

namespace PraxisFloeckinger.Infrastructure.Persistence.Tenant;

/// <summary>
/// DbContext für eine einzelne Tenant-Datenbank (<c>tenant_&lt;guid&gt;</c>).
/// Jede Praxis hat ihre eigene Instanz dieser DB — Patientendaten verlassen nie
/// die jeweilige Tenant-DB.
/// </summary>
public sealed class TenantDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public TenantDbContext(DbContextOptions<TenantDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();
    public DbSet<TherapistProfile> TherapistProfiles => Set<TherapistProfile>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<TwoFactorRecoveryCode> TwoFactorRecoveryCodes => Set<TwoFactorRecoveryCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureUser(modelBuilder);
        ConfigurePatientProfile(modelBuilder);
        ConfigureTherapistProfile(modelBuilder);
        ConfigureRefreshToken(modelBuilder);
        ConfigureTwoFactorRecoveryCode(modelBuilder);
    }

    /// <summary>
    /// Setzt automatisch Audit-Felder:
    /// <list type="bullet">
    ///   <item>Modified entries → <c>ModifiedAt = UtcNow</c></item>
    ///   <item>IsDeleted-Übergang → <c>DeletedAt = UtcNow</c></item>
    /// </list>
    /// TODO: ModifiedBy/DeletedBy aus ICurrentUserAccessor setzen sobald implementiert.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<EntityBase>()
            .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.ModifiedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<SoftDeletableEntityBase>()
            .Where(e => e.State == EntityState.Modified
                     && e.Entity.IsDeleted
                     && e.OriginalValues.GetValue<bool>(nameof(SoftDeletableEntityBase.IsDeleted)) == false))
        {
            entry.Entity.DeletedAt = now;
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Email)
                  .HasMaxLength(320)
                  .IsRequired();
            entity.HasIndex(e => e.Email)
                  .IsUnique()
                  .HasDatabaseName("IX_Users_Email");

            entity.Property(e => e.PasswordHash)
                  .HasMaxLength(500)
                  .IsRequired();

            entity.Property(e => e.Role)
                  .HasConversion<int>()
                  .IsRequired();

            entity.Property(e => e.FirstName)
                  .HasMaxLength(100)
                  .IsRequired();

            entity.Property(e => e.LastName)
                  .HasMaxLength(100)
                  .IsRequired();

            entity.Property(e => e.Phone)
                  .HasMaxLength(50);

            entity.Property(e => e.TotpSecret)
                  .HasMaxLength(500); // AES-GCM verschlüsselt: Base64(12+n+16) > Klartext

            entity.Property(e => e.MustRotateRecoveryCodes)
                  .HasDefaultValue(false);

            entity.Property(e => e.FailedLoginAttempts)
                  .HasDefaultValue(0);

            entity.Property(e => e.IsDeleted)
                  .HasDefaultValue(false);

            entity.HasQueryFilter(e => !e.IsDeleted);
        });
    }

    private static void ConfigurePatientProfile(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PatientProfile>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Address)
                  .HasMaxLength(500)
                  .IsRequired();

            entity.Property(e => e.EmergencyContact)
                  .HasMaxLength(500);

            entity.Property(e => e.InsuranceInfo)
                  .HasMaxLength(200);

            entity.Property(e => e.PublicNotes)
                  .HasMaxLength(2000);

            entity.Property(e => e.IsDeleted)
                  .HasDefaultValue(false);

            entity.HasQueryFilter(e => !e.IsDeleted);

            entity.HasOne<User>()
                  .WithOne()
                  .HasForeignKey<PatientProfile>(p => p.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureTherapistProfile(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TherapistProfile>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Specializations)
                  .HasColumnType("text[]");

            // PostgreSQL: max 20 Einträge; max 100 Zeichen pro Eintrag liegt bei der Anwendung
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_TherapistProfiles_Specializations_MaxCount",
                "array_length(\"Specializations\", 1) IS NULL OR array_length(\"Specializations\", 1) <= 20"));

            entity.Property(e => e.Bio)
                  .HasMaxLength(5000);

            entity.Property(e => e.IsDeleted)
                  .HasDefaultValue(false);

            entity.HasQueryFilter(e => !e.IsDeleted);

            entity.HasOne<User>()
                  .WithOne()
                  .HasForeignKey<TherapistProfile>(p => p.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureRefreshToken(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.TokenHash)
                  .HasMaxLength(64)
                  .IsRequired();
            entity.HasIndex(e => e.TokenHash)
                  .IsUnique()
                  .HasDatabaseName("IX_RefreshTokens_TokenHash");

            entity.HasIndex(e => e.UserId)
                  .HasDatabaseName("IX_RefreshTokens_UserId");

            entity.Property(e => e.RevokedReason)
                  .HasMaxLength(100);

            entity.Property(e => e.CreatedFromIp)
                  .HasMaxLength(64)
                  .IsRequired();

            entity.Property(e => e.CreatedFromUserAgent)
                  .HasMaxLength(512)
                  .IsRequired();

            entity.Property(e => e.IsDeleted)
                  .HasDefaultValue(false);

            entity.HasQueryFilter(e => !e.IsDeleted);

            entity.HasOne<User>()
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureTwoFactorRecoveryCode(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TwoFactorRecoveryCode>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CodeHash)
                  .HasMaxLength(500)
                  .IsRequired();

            entity.HasIndex(e => e.UserId)
                  .HasDatabaseName("IX_TwoFactorRecoveryCodes_UserId");

            entity.Property(e => e.IsDeleted)
                  .HasDefaultValue(false);

            entity.HasQueryFilter(e => !e.IsDeleted);

            entity.HasOne<User>()
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
