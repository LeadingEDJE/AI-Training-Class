using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

// Compass's own entities. Aliased rather than imported bare: this file sits in a namespace ending
// `.Compass`, so an unqualified `using LeadingEDJE.Leap.Api.Modules.Compass;` reads as though it
// referred to the test namespace itself.
using CompassEmployee = LeadingEDJE.Leap.Api.Modules.Compass.Employee;
using CompassEmployeeType = LeadingEDJE.Leap.Api.Modules.Compass.EmployeeType;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// Verifies the effective-roles half of the audit contract (issue #56) against the LIVE Postgres
/// catalog and LIVE column behaviour — not against the generated migration, which looks correct
/// either way.
/// </summary>
/// <remarks>
/// <para>
/// The unit-level semantics live in <c>tests/unit/Compass/AuditEffectiveRolesTests.cs</c>. What can
/// only be proven here is that Postgres itself carries the three-value distinction: a
/// <c>text[]</c> column that is nullable and carries NO default, so <c>NULL</c> ("not captured")
/// survives as a value genuinely distinct from <c>'{}'</c> ("captured, no roles held"). A column
/// default would silently collapse the two, and no unit test can see that.
/// </para>
/// <para>
/// A Postgres array-mapped collection property is the first in this model — every other multi-value
/// column is a pre-serialized string in <c>jsonb</c>. So the storage type is asserted from
/// <c>information_schema</c> rather than assumed from the CLR type.
/// </para>
/// </remarks>
public class CompassAuditTests(IntegrationTestFactory factory) : IntegrationTestBase(factory)
{
    private const string OpsActor = "33333333-3333-3333-3333-333333333333";

    private const int EmployeeTypeId = 1;

    /// <summary>Seeds one active Compass directory employee and returns its id.</summary>
    private async Task<int> SeedEmployeeAsync()
    {
        const int employeeId = 1;
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (!await db.Set<CompassEmployeeType>().AnyAsync(TestContext.Current.CancellationToken))
        {
            db.Set<CompassEmployeeType>().Add(new CompassEmployeeType
            {
                Id = EmployeeTypeId,
                TypeName = "Full Time",
                IsActive = true,
            });
        }

        db.Set<CompassEmployee>().Add(new CompassEmployee
        {
            Id = employeeId,
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"compass-{employeeId}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return employeeId;
    }

    private static AuditLog CompassWrite(string entityId, List<string>? effectiveRoles) =>
        new()
        {
            EntityType = "CompassClientAssignment",
            EntityId = entityId,
            Action = "Update",
            Actor = OpsActor,
            TriggeredBy = "UI",
            Reason = "Corrected the assignment end date",
            Changes = "{}",
            Timestamp = DateTime.UtcNow,
            EffectiveRoles = effectiveRoles
        };

    [Fact]
    public async Task EffectiveRolesColumn_IsANullableTextArrayWithNoDefault()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act — read the catalog, because the three-value contract is a property of the COLUMN.
        var shape = await context
            .Database.SqlQueryRaw<string>(
                @"SELECT (data_type || '|' || udt_name || '|' || is_nullable || '|'
                          || coalesce(column_default, 'NONE')) AS ""Value""
                   FROM information_schema.columns
                   WHERE table_schema = 'public' AND table_name = 'audit_logs'
                     AND column_name = 'effective_roles';")
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        shape.ShouldHaveSingleItem("audit_logs must carry an effective_roles column");
        shape[0].ShouldBe(
            "ARRAY|_text|YES|NONE",
            "effective_roles must be a NULLABLE text[] with NO default — a default would collapse "
                + "NULL ('not captured') into '{}' ('captured, no roles held'), which is the whole "
                + "distinction the field exists to carry");
    }

    [Fact]
    public async Task EffectiveRoles_RoundTripThroughPostgres_PreserveTheRecordedRoleSet()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        context.AuditLogs.Add(CompassWrite("101", [RolePolicy.CompassOpsRole, RolePolicy.EDJEr]));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        AuditLog stored = await context.AuditLogs
            .AsNoTracking()
            .SingleAsync(a => a.EntityId == "101", TestContext.Current.CancellationToken);
        stored.EffectiveRoles.ShouldNotBeNull();
        stored.EffectiveRoles.ShouldBe([RolePolicy.CompassOpsRole, RolePolicy.EDJEr], ignoreOrder: true);
    }

    [Fact]
    public async Task EffectiveRoles_NullAndEmptyArray_RemainDistinguishableInTheDatabase()
    {
        // Arrange — this is the assertion the whole field design turns on.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.AuditLogs.Add(CompassWrite("not-captured", null));
        context.AuditLogs.Add(CompassWrite("no-roles-held", []));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act — ask Postgres, not EF: "not captured" and "captured but empty" must select different
        // rows. Spelled with cardinality rather than the literal `= '{}'` because SqlQueryRaw passes
        // the SQL through String.Format, which parses a bare `{}` as a format placeholder and throws.
        var notCaptured = await context
            .Database.SqlQueryRaw<string>(
                @"SELECT entity_id AS ""Value"" FROM audit_logs WHERE effective_roles IS NULL;")
            .ToListAsync(TestContext.Current.CancellationToken);
        var capturedButEmpty = await context
            .Database.SqlQueryRaw<string>(
                @"SELECT entity_id AS ""Value"" FROM audit_logs
                   WHERE effective_roles IS NOT NULL AND cardinality(effective_roles) = 0;")
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        notCaptured.ShouldContain("not-captured");
        notCaptured.ShouldNotContain("no-roles-held");
        capturedButEmpty.ShouldContain("no-roles-held");
        capturedButEmpty.ShouldNotContain("not-captured");
    }

    [Fact]
    public async Task EffectiveRoles_AreQueryableAsASet_NotAsProse()
    {
        // Arrange — "what did anyone acting as Super Admin change?" is the reason this is text[]
        // rather than a delimited string, where set membership would degrade to LIKE.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.AuditLogs.Add(CompassWrite("as-ops", [RolePolicy.CompassOpsRole]));
        context.AuditLogs.Add(CompassWrite("as-root", [RolePolicy.CompassOpsRole, RolePolicy.CompassSuperAdminRole]));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var actedAsRoot = await context
            .Database.SqlQueryRaw<string>(
                @"SELECT entity_id AS ""Value"" FROM audit_logs
                   WHERE effective_roles @> ARRAY['Compass Super Admin'];")
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        actedAsRoot.ShouldHaveSingleItem();
        actedAsRoot[0].ShouldBe("as-root");
    }

    [Fact]
    public async Task HistoricalEntry_StillReportsTheAuthorityHeldWhenItWasWritten()
    {
        // Arrange — FR-027. Roles are never stored (BR-12) and group membership drifts, so the entry
        // must hold a COPY. Here the same actor makes a second change after gaining the root role.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.AuditLogs.Add(CompassWrite("earlier", [RolePolicy.CompassOpsRole]));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act — membership changes, and the same actor writes again under the new authority.
        context.AuditLogs.Add(CompassWrite("later", [RolePolicy.CompassOpsRole, RolePolicy.CompassSuperAdminRole]));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        AuditLog earlier = await context.AuditLogs
            .AsNoTracking()
            .SingleAsync(a => a.EntityId == "earlier", TestContext.Current.CancellationToken);
        earlier.EffectiveRoles.ShouldBe([RolePolicy.CompassOpsRole]);
    }

    [Fact]
    public async Task RecordedAuthority_CannotBeRewrittenAfterTheFact()
    {
        // Arrange — the append-only triggers already refuse UPDATE generally; this pins the guarantee
        // to the new column specifically, because an audit field that can be edited proves nothing.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.AuditLogs.Add(CompassWrite("immutable", [RolePolicy.CompassOpsRole]));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act & Assert
        var ex = await Should.ThrowAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlRawAsync(
                @"UPDATE audit_logs SET effective_roles = ARRAY['Compass Super Admin']
                   WHERE entity_id = 'immutable';",
                TestContext.Current.CancellationToken));
        ex.Message.ShouldContain("cannot be modified", Case.Insensitive);
    }

    [Fact]
    public async Task CompassRead_ProducesNoAuditEntry()
    {
        // Arrange — read auditing is explicitly out of scope for v1 (ADR-005) and must not creep in.
        // Compass's only surface today is a read, so this is the one place the rule can be exercised.
        //
        // The employee is SEEDED and the read SUCCEEDS on purpose. Reading a random id returns null on
        // the not-found branch before reaching any code an audit call would live in, so it would pass
        // even after someone added read auditing to the success path — the exact creep this guards.
        await ResetDatabaseAsync();
        int employeeId = await SeedEmployeeAsync();
        // A bare DI scope has no HTTP context, and IDirectory now resolves the caller's tier
        // (spec 009 Slice 1) -- construct the service directly with a stub, per
        // NoHttpContextCurrentUser's remarks, rather than resolve IDirectory from DI.
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        IDirectory directory = new CompassDirectoryService(
            scope.ServiceProvider.GetRequiredService<ICompassDirectoryRepository>(),
            new NoHttpContextCurrentUser());
        int before = await context.AuditLogs.CountAsync(TestContext.Current.CancellationToken);

        // Act
        var employee = await directory.GetEmployeeAsync(
            employeeId, TestContext.Current.CancellationToken);

        // Assert
        employee.ShouldNotBeNull("the read must actually return a record, or this proves nothing");
        int after = await context.AuditLogs.CountAsync(TestContext.Current.CancellationToken);
        after.ShouldBe(before, "a Compass read must write no audit entry");
    }
}
