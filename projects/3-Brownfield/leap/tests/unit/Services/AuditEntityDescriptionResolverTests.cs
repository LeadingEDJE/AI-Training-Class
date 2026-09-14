using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Covers <see cref="AuditEntityDescriptionResolver"/> across every entity type it still resolves
/// (Person, UserRole, SystemSetting, EmployeeAttribute/Impersonate person-key lookups) after the
/// Timesheet/Ooto module retirement dropped the Timesheet/EmployeeYearBalance/Client/Assignment/
/// JobTitle/EmploymentRecord cases along with the tables they described.
/// </summary>
public class AuditEntityDescriptionResolverTests : IDisposable
{
    private readonly LeapDbContext _context;
    private readonly AuditEntityDescriptionResolver _resolver;

    private readonly Guid _personId = Guid.NewGuid();
    private readonly Guid _personEdjeId = Guid.NewGuid();

    public AuditEntityDescriptionResolverTests()
    {
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new LeapDbContext(options);
        _resolver = new AuditEntityDescriptionResolver(_context);

        _context.People.Add(new Person
        {
            Id = _personId,
            EdjeId = _personEdjeId,
            FirstName = "Avery",
            LastName = "Quinn"
        });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private static AuditLog Row(string entityType, string entityId) => new()
    {
        EntityType = entityType,
        EntityId = entityId,
        Action = "update",
        Actor = "system",
        TriggeredBy = "test",
        Reason = "r",
        Changes = "[]",
        Timestamp = DateTime.UtcNow
    };

    private async Task<string?> DescribeAsync(string entityType, string entityId)
    {
        var row = Row(entityType, entityId);
        var descriptions = await _resolver.ResolveAsync([row]);
        return descriptions.TryGetValue(AuditEntityDescriptionResolver.Key(entityType, entityId), out var d)
            ? d
            : null;
    }

    [Fact]
    public async Task Resolve_Person_DescribesThePerson()
    {
        // Act — AdminUserRoleEndpoints/AuditService log Person.Id
        var description = await DescribeAsync("Person", _personId.ToString());

        // Assert
        description.ShouldBe("Avery Quinn");
    }

    [Fact]
    public async Task Resolve_EmployeeAttribute_DescribesTheEmployeeBehindTheEdjeId()
    {
        // Act — a caller logging Person.EdjeId as the entity id
        var description = await DescribeAsync("EmployeeAttribute", _personEdjeId.ToString());

        // Assert
        description.ShouldBe("Avery Quinn");
    }

    [Fact]
    public async Task Resolve_Impersonate_DescribesTheTargetEmployee()
    {
        // Act
        var description = await DescribeAsync("Impersonate", _personEdjeId.ToString());

        // Assert
        description.ShouldBe("Avery Quinn");
    }

    [Fact]
    public async Task Resolve_ReturnsNull_WhenThePersonHasNoName()
    {
        // Arrange — a person row with neither first nor last name yields no usable label, so the
        // UI must fall back to the raw id rather than render a blank cell.
        var namelessPersonId = Guid.NewGuid();
        _context.People.Add(new Person
        {
            Id = namelessPersonId,
            EdjeId = Guid.NewGuid(),
            FirstName = null,
            LastName = null
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var description = await DescribeAsync("Person", namelessPersonId.ToString());

        // Assert
        description.ShouldBeNull();
    }

    [Fact]
    public async Task Resolve_UserRole_DescribesEmployeeAndRole()
    {
        // Act — UserRoleService logs "{edjeId}:{role}"
        var description = await DescribeAsync("UserRole", $"{_personEdjeId}:Manager");

        // Assert
        description.ShouldBe("Avery Quinn — Manager");
    }

    [Fact]
    public async Task Resolve_SystemSetting_DescribesTheSettingDescription()
    {
        // Arrange
        _context.SystemSettings.Add(new SystemSetting
        {
            Key = "audit.retention.years",
            Value = "2",
            Description = "Years an audit entry is retained before purge"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var description = await DescribeAsync("SystemSetting", "audit.retention.years");

        // Assert
        description.ShouldBe("Years an audit entry is retained before purge");
    }

    [Fact]
    public async Task Resolve_ReturnsNull_ForAnUnknownEntityType()
    {
        // Act — an unmapped type must degrade to the raw id, not an empty cell
        var description = await DescribeAsync("SomethingNew", "abc");

        // Assert
        description.ShouldBeNull();
    }

    [Fact]
    public async Task Resolve_ReturnsNull_WhenTheTargetRowIsGone()
    {
        // Act
        var description = await DescribeAsync("Person", Guid.NewGuid().ToString());

        // Assert
        description.ShouldBeNull();
    }

    [Fact]
    public async Task Resolve_BatchesAcrossMixedEntityTypesInOneCall()
    {
        // Arrange — a real page mixes types; every row must come back described
        _context.SystemSettings.Add(new SystemSetting { Key = "some.setting", Value = "x", Description = "Some setting" });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rows = new[]
        {
            Row("Person", _personId.ToString()),
            Row("SystemSetting", "some.setting"),
            Row("UnknownType", "abc")
        };

        // Act
        var descriptions = await _resolver.ResolveAsync(rows);

        // Assert
        descriptions[AuditEntityDescriptionResolver.Key("Person", _personId.ToString())]
            .ShouldBe("Avery Quinn");
        descriptions[AuditEntityDescriptionResolver.Key("SystemSetting", "some.setting")]
            .ShouldBe("Some setting");
        descriptions.ContainsKey(AuditEntityDescriptionResolver.Key("UnknownType", "abc"))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Resolve_ReturnsEmpty_ForNoRows()
    {
        // Act
        var descriptions = await _resolver.ResolveAsync([]);

        // Assert
        descriptions.Count.ShouldBe(0);
    }
}
