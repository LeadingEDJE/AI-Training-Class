using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data.Configurations;

public class EntityConfigurationTests : IDisposable
{
    private readonly LeapDbContext _context;

    public EntityConfigurationTests()
    {
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .UseSnakeCaseNamingConvention()
            .Options;
        _context = new LeapDbContext(options);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    // --- Entity Registration Tests ---
    // Narrowed to the Platform entities that survived the Timesheet/Ooto module retirement (Compass
    // owns and configures its own entity set separately).

    [Fact]
    public void Model_ContainsAllFivePlatformEntities()
    {
        // Act & Assert
        _context.Model.FindEntityType(typeof(AuditLog)).ShouldNotBeNull();
        _context.Model.FindEntityType(typeof(SystemSetting)).ShouldNotBeNull();
        _context.Model.FindEntityType(typeof(NotificationLog)).ShouldNotBeNull();
        _context.Model.FindEntityType(typeof(UserRole)).ShouldNotBeNull();
        _context.Model.FindEntityType(typeof(Person)).ShouldNotBeNull();
    }

    // --- Table Name Tests (snake_case via UseSnakeCaseNamingConvention) ---

    [Fact]
    public void PlatformEntities_HaveSnakeCaseTableNames()
    {
        // Arrange - expected table names from UseSnakeCaseNamingConvention over the entity CLR type
        // name, except Person, whose table name ("people") is set explicitly in PersonConfiguration.
        // The InMemory provider this fixture uses does not register the relational DbSet-name-based
        // pluralizing convention (that convention lives in the relational conventions set), so these
        // stay singular even though the real Npgsql-backed model (see the generated migrations) names
        // the same tables "audit_logs", "system_settings", etc.
        var expectedTableNames = new Dictionary<Type, string>
        {
            { typeof(AuditLog), "audit_log" },
            { typeof(SystemSetting), "system_setting" },
            { typeof(NotificationLog), "notification_log" },
            { typeof(UserRole), "user_role" },
            { typeof(Person), "people" },
        };

        // Act & Assert
        foreach (var (entityClrType, expectedName) in expectedTableNames)
        {
            var entityType = _context.Model.FindEntityType(entityClrType);
            entityType.ShouldNotBeNull($"Entity type {entityClrType.Name} not found in model");
            var actualName = entityType.GetTableName();
            actualName.ShouldBe(expectedName, $"Table name mismatch for {entityClrType.Name}");
        }
    }

    // --- Audit Field Tests (AuditableEntity-derived types) ---

    [Fact]
    public void AllAuditableEntities_HaveCreatedAtAndUpdatedAtProperties()
    {
        // Arrange - SystemSetting, NotificationLog and UserRole derive from AuditableEntity; Person
        // predates that convention (see PersonConfiguration) and is deliberately excluded.
        var auditableTypes = new[]
        {
            typeof(SystemSetting),
            typeof(NotificationLog),
            typeof(UserRole),
        };

        // Act & Assert
        foreach (var type in auditableTypes)
        {
            var entityType = _context.Model.FindEntityType(type)!;
            entityType.FindProperty(nameof(AuditableEntity.CreatedAt))
                .ShouldNotBeNull($"{type.Name} missing CreatedAt");
            entityType.FindProperty(nameof(AuditableEntity.UpdatedAt))
                .ShouldNotBeNull($"{type.Name} missing UpdatedAt");
            entityType.FindProperty(nameof(AuditableEntity.CreatedBy))
                .ShouldNotBeNull($"{type.Name} missing CreatedBy");
            entityType.FindProperty(nameof(AuditableEntity.UpdatedBy))
                .ShouldNotBeNull($"{type.Name} missing UpdatedBy");
        }
    }

    // --- Person provenance marker (quick task 260729-sqc, D-07) ---

    [Fact]
    public void PersonSource_IsNullableSnakeCaseColumnBoundedTo64Chars()
    {
        // Arrange
        var person = _context.Model.FindEntityType(typeof(Person))!;

        // Act
        var source = person.FindProperty(nameof(Person.Source));

        // Assert — nullable diagnostic marker, snake_case "source", bounded (PersonSource constants).
        source.ShouldNotBeNull();
        source.IsNullable.ShouldBeTrue();
        source.GetColumnName().ShouldBe("source");
        source.GetMaxLength().ShouldBe(64);
    }

    [Fact]
    public void PersonSource_IsNotIndexed_BecauseItIsDiagnosticNotALookupKey()
    {
        // Arrange
        var person = _context.Model.FindEntityType(typeof(Person))!;

        // Act
        var indexedOnSource = person.GetIndexes()
            .Any(i => i.Properties.Any(p => p.Name == nameof(Person.Source)));

        // Assert
        indexedOnSource.ShouldBeFalse();
    }
}
