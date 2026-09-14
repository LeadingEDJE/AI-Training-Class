using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LeadingEDJE.Leap.Api.Platform.Data;

/// <summary>Central EF Core DbContext for the application, providing entity sets, audit-field stamping, and a Npgsql convention that maps every DateTime property to 'timestamp without time zone'.</summary>
public class LeapDbContext(DbContextOptions<LeapDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>Append-only audit trail for entity changes.</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    /// <summary>Application-wide key-value configuration settings.</summary>
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    /// <summary>Slack and email notification delivery records with idempotency keys.</summary>
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    /// <summary>Additive role assignments outside of IdP claims.</summary>
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    /// <summary>People absorbed from the deprecated TPS system — identity system-of-record.</summary>
    public DbSet<Person> People => Set<Person>();

    /// <summary>
    /// ASP.NET Core Data Protection key ring, persisted so cookie sessions survive API pod restarts
    /// and replica switches. Satisfies <see cref="IDataProtectionKeyContext"/> for the wiring in <c>Program.cs</c>.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Applies all IEntityTypeConfiguration&lt;T&gt; implementations from this assembly and maps every DateTime/DateTime? property to Postgres 'timestamp without time zone', preserving the legacy UTC-naive datetime semantics and avoiding the Npgsql timestamptz write-time Kind exception. DateOnly maps to native Postgres 'date' with no converter.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LeapDbContext).Assembly);

        // Every DateTime/DateTime? property maps to 'timestamp without time zone', preserving the
        // legacy UTC-naive semantics. Npgsql enforces Kind at write time in both directions and
        // 'timestamp without time zone' rejects Kind==Utc, but the app stamps DateTime.UtcNow, so the
        // converter strips Kind to Unspecified at the provider boundary and reads come back
        // Unspecified. DateOnly maps to native 'date' with no converter.
        var stripKindConverter = new ValueConverter<DateTime, DateTime>(
            v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified),
            v => v);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property.SetColumnType("timestamp without time zone");
                    property.SetValueConverter(stripKindConverter);
                }
            }
        }
    }

    /// <summary>Stamps CreatedAt/CreatedBy and UpdatedAt/UpdatedBy on all AuditableEntity instances in the change tracker, then delegates to the base implementation.</summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
                entry.Entity.CreatedBy ??= "system";
                entry.Entity.UpdatedBy ??= "system";
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy ??= "system";
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
