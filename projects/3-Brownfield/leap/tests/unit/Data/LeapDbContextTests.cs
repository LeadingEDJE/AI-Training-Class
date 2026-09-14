using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

public class LeapDbContextTests : IDisposable
{
    private readonly LeapDbContext _context;

    public LeapDbContextTests()
    {
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new LeapDbContext(options);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public void OnModelCreating_ConfiguresDbSets()
    {
        // Assert - verify key DbSet properties are accessible (configurations applied)
        _context.AuditLogs.ShouldNotBeNull();
        _context.SystemSettings.ShouldNotBeNull();
        _context.NotificationLogs.ShouldNotBeNull();
        _context.UserRoles.ShouldNotBeNull();
        _context.People.ShouldNotBeNull();
        _context.DataProtectionKeys.ShouldNotBeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_SetsCreatedAtAndUpdatedAt_OnAdd()
    {
        // Arrange
        var role = new UserRole { EdjeId = Guid.NewGuid(), Role = "EDJEr" };

        // Act
        _context.UserRoles.Add(role);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        role.CreatedAt.ShouldNotBe(default);
        role.UpdatedAt.ShouldNotBe(default);
        role.CreatedBy.ShouldBe("system");
    }

    [Fact]
    public async Task SaveChangesAsync_UpdatesUpdatedAt_OnModify()
    {
        // Arrange
        var role = new UserRole { EdjeId = Guid.NewGuid(), Role = "EDJEr" };
        _context.UserRoles.Add(role);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var originalUpdatedAt = role.UpdatedAt;

        // Small delay to ensure timestamp difference
        await Task.Delay(10, TestContext.Current.CancellationToken);

        // Act
        role.Role = "Manager";
        _context.Entry(role).State = EntityState.Modified;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        role.UpdatedAt.ShouldBeGreaterThan(originalUpdatedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_SetsDefaultCreatedBy_WhenNull()
    {
        // Arrange
        var role = new UserRole
        {
            EdjeId = Guid.NewGuid(),
            Role = "EDJEr",
            CreatedBy = null!,
            UpdatedBy = null!
        };

        // Act
        _context.UserRoles.Add(role);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        role.CreatedBy.ShouldBe("system");
        role.UpdatedBy.ShouldBe("system");
    }

    [Fact]
    public void Model_HasNoDateOnlyConverter_MapsDateOnlyNativelyToPostgresDate()
    {
        // Arrange & Act - ensure the model is built (triggers OnModelCreating)
        var model = _context.Model;

        // Assert - Npgsql maps DateOnly -> Postgres 'date' natively, so there must be
        // NO value converter (the Oracle-MySQL DateOnly->DateTime converter was removed
        // in Phase 42's Postgres replatform).
        var notificationLogEntityType = model.FindEntityType(typeof(NotificationLog));
        notificationLogEntityType.ShouldNotBeNull();

        var periodWeekStartProp = notificationLogEntityType.FindProperty(nameof(NotificationLog.PeriodWeekStart));
        periodWeekStartProp.ShouldNotBeNull();
        periodWeekStartProp.ClrType.ShouldBe(typeof(DateOnly));
        periodWeekStartProp.GetValueConverter().ShouldBeNull("DateOnly maps natively to Postgres date -- no converter");
    }

    [Fact]
    public void Model_MapsAuditableDateTimeColumns_ToTimestampWithoutTimeZone()
    {
        // Arrange & Act
        var model = _context.Model;

        // Assert - UserRole inherits from AuditableEntity. The DateTime convention
        // maps CreatedAt/UpdatedAt to 'timestamp without time zone' (preserving UTC-naive
        // legacy semantics and avoiding the Npgsql timestamptz write-time Kind exception).
        var roleEntity = model.FindEntityType(typeof(UserRole));
        roleEntity.ShouldNotBeNull();

        // Read the raw relational annotation (provider-agnostic): the strongly-typed
        // GetColumnType() extension throws under the InMemory provider, which is not
        // relational. The annotation is what the Npgsql migration generator consumes.
        var createdAtProp = roleEntity.FindProperty(nameof(AuditableEntity.CreatedAt));
        createdAtProp.ShouldNotBeNull();
        createdAtProp.ClrType.ShouldBe(typeof(DateTime));
        var createdAtColumnType = createdAtProp.FindAnnotation("Relational:ColumnType");
        createdAtColumnType.ShouldNotBeNull();
        createdAtColumnType.Value.ShouldBe("timestamp without time zone");

        var updatedAtProp = roleEntity.FindProperty(nameof(AuditableEntity.UpdatedAt));
        updatedAtProp.ShouldNotBeNull();
        updatedAtProp.ClrType.ShouldBe(typeof(DateTime));
        var updatedAtColumnType = updatedAtProp.FindAnnotation("Relational:ColumnType");
        updatedAtColumnType.ShouldNotBeNull();
        updatedAtColumnType.Value.ShouldBe("timestamp without time zone");
    }

    [Fact]
    public void Model_DateTimeConverter_StripsUtcKind_ForTimestampWithoutTimeZone()
    {
        // Arrange & Act
        var model = _context.Model;

        // Assert - Npgsql THROWS at write time when a DateTime with Kind=Utc is written
        // to a 'timestamp without time zone' column, and the app stamps timestamps with
        // DateTime.UtcNow. The convention must strip Kind to Unspecified at the provider
        // boundary (legacy MySQL 'datetime' reads also returned Unspecified, so this is
        // exact behavior parity).
        var roleEntity = model.FindEntityType(typeof(UserRole));
        roleEntity.ShouldNotBeNull();

        var createdAtProp = roleEntity.FindProperty(nameof(AuditableEntity.CreatedAt));
        createdAtProp.ShouldNotBeNull();
        var converter = createdAtProp.GetValueConverter();
        converter.ShouldNotBeNull("Npgsql rejects Kind=Utc writes to 'timestamp without time zone' -- the convention must strip Kind at the provider boundary");

        var utcValue = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);
        var providerValue = (DateTime)converter.ConvertToProvider(utcValue)!;
        providerValue.Kind.ShouldBe(DateTimeKind.Unspecified);
        providerValue.ShouldBe(DateTime.SpecifyKind(utcValue, DateTimeKind.Unspecified));

        // Nullable DateTime properties get the same converter via the convention loop.
        var notificationLogEntityType = model.FindEntityType(typeof(NotificationLog));
        notificationLogEntityType.ShouldNotBeNull();
        var sentAtProp = notificationLogEntityType.FindProperty(nameof(NotificationLog.SentAt));
        sentAtProp.ShouldNotBeNull();
        sentAtProp.GetValueConverter().ShouldNotBeNull();
    }

    [Fact]
    public void Model_MapsJsonColumns_ToJsonb()
    {
        // Arrange & Act
        var model = _context.Model;

        // Assert - JSON columns map to Postgres 'jsonb' (Phase 42 replatform dropped the
        // MySQL 'json' + HasMaxLength combination). Read the raw relational annotation
        // because the InMemory provider is non-relational.
        var auditLogEntity = model.FindEntityType(typeof(AuditLog));
        auditLogEntity.ShouldNotBeNull();
        var changesColumnType = auditLogEntity.FindProperty(nameof(AuditLog.Changes))!
            .FindAnnotation("Relational:ColumnType");
        changesColumnType.ShouldNotBeNull();
        changesColumnType.Value.ShouldBe("jsonb");
    }
}
