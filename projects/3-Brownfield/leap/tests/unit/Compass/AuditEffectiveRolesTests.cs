using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// Covers the effective-roles half of the audit contract (issue #56, AC-NFR-3, review observation O5):
/// an audit entry must record not only WHO made a change but the AUTHORITY they made it under.
/// </summary>
/// <remarks>
/// <para>
/// Why the field is needed at all. <c>CompassAuthorizationExtensions</c> wires
/// <c>Compass Super Admin</c> to satisfy every other Compass policy, so once the root can do
/// everything Ops can, capability no longer distinguishes the two roles — only the audit record does.
/// Role membership is never persisted (BR-12) and Google group membership drifts over time, so
/// <c>Actor</c> alone leaves the authority behind a historical change unreconstructable.
/// </para>
/// <para>
/// The three-value contract these tests pin down. <c>null</c> means NOT CAPTURED (a Timesheet
/// or OOTO write, or a row predating the migration); an empty set means CAPTURED AND THE ACTOR HELD NO
/// ROLES; a populated set means captured with those roles. Collapsing the first two would silently
/// conflate "we did not record the authority" with "there was no authority", which is the entire point
/// of the field. That is also why the column carries no default — see
/// <see cref="AuditLog.EffectiveRoles"/>.
/// </para>
/// </remarks>
public class AuditEffectiveRolesTests : IDisposable
{
    private readonly IAuditService _service;
    private readonly InMemoryAuditLogRepository _repository;
    private readonly LeapDbContext _context;

    public AuditEffectiveRolesTests()
    {
        _repository = new InMemoryAuditLogRepository();
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new LeapDbContext(options);
        _service = new AuditService(_repository, _context, new InMemoryEmployeeDirectory());
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<AuditLog> LogAndReadBackAsync(AuditEntry entry)
    {
        await _service.LogAsync(entry);
        var logs = (await _repository.GetAllAsync()).ToList();
        logs.Count.ShouldBe(1);
        return logs[0];
    }

    private static AuditEntry CompassWrite(
        List<string>? effectiveRoles,
        string actor = "11111111-1111-1111-1111-111111111111",
        string entityId = "7") =>
        new(
            EntityType: "CompassClientAssignment",
            EntityId: entityId,
            Action: "Update",
            Actor: actor,
            TriggeredBy: "UI",
            Reason: "Corrected the assignment end date",
            Changes: [new FieldChange("EndDate", null, "2026-09-30")],
            EffectiveRoles: effectiveRoles);

    [Fact]
    public async Task LogAsync_WithEffectiveRoles_RecordsTheRolesHeldAtWriteTime()
    {
        // Arrange
        var entry = CompassWrite([RolePolicy.CompassOpsRole, RolePolicy.EDJEr]);

        // Act
        AuditLog log = await LogAndReadBackAsync(entry);

        // Assert
        log.EffectiveRoles.ShouldNotBeNull();
        log.EffectiveRoles.ShouldBe([RolePolicy.CompassOpsRole, RolePolicy.EDJEr], ignoreOrder: true);
    }

    [Fact]
    public async Task LogAsync_WithoutEffectiveRoles_LeavesThemNullSoTimesheetAndOotoWritesAreUnchanged()
    {
        // Arrange — the seven-argument shape every existing Timesheet and OOTO call site uses.
        // FR-043 scopes capture to Compass, so those writes must continue to record nothing rather
        // than recording an empty set, which would claim the actor held no roles.
        var entry = new AuditEntry(
            EntityType: "Timesheet",
            EntityId: "42",
            Action: "Approve",
            Actor: "22222222-2222-2222-2222-222222222222",
            TriggeredBy: "ManagerAction",
            Reason: "Approved for the pay period",
            Changes: [new FieldChange("Status", "Submitted", "Approved")]);

        // Act
        AuditLog log = await LogAndReadBackAsync(entry);

        // Assert
        log.EffectiveRoles.ShouldBeNull();
    }

    [Fact]
    public async Task LogAsync_WhenTheActorHeldNoRoles_RecordsAnEmptySetDistinctFromNotCaptured()
    {
        // Arrange
        var entry = CompassWrite([]);

        // Act
        AuditLog log = await LogAndReadBackAsync(entry);

        // Assert — captured, and the answer was "none". NOT the same as never having looked.
        log.EffectiveRoles.ShouldNotBeNull();
        log.EffectiveRoles.ShouldBeEmpty();
    }

    [Fact]
    public async Task LogAsync_StoresEffectiveRolesSorted_SoTwoWritesUnderTheSameAuthorityCompareEqual()
    {
        // Arrange — the same authority, presented in two different orders. Privileges come from a
        // HashSet in CurrentUserContext, whose enumeration order is not guaranteed stable, so without
        // a sort two entries made under identical authority would not compare equal.
        var first = CompassWrite([RolePolicy.CompassSuperAdminRole, RolePolicy.CompassOpsRole], entityId: "1");
        var second = CompassWrite([RolePolicy.CompassOpsRole, RolePolicy.CompassSuperAdminRole], entityId: "2");

        // Act
        await _service.LogAsync(first);
        await _service.LogAsync(second);

        // Assert
        var logs = (await _repository.GetAllAsync()).ToList();
        logs.Count.ShouldBe(2);
        logs[0].EffectiveRoles.ShouldBe(logs[1].EffectiveRoles);
        logs[0].EffectiveRoles.ShouldBe([RolePolicy.CompassOpsRole, RolePolicy.CompassSuperAdminRole]);
    }

    [Fact]
    public async Task LogAsync_CopiesEffectiveRoles_SoMutatingTheCallersListLeavesTheEntryUnchanged()
    {
        // Arrange — FR-027 requires a point-in-time COPY, never a reference. Mutating the caller's
        // list after the write stands in for the real hazard: anything that holds a live reference to
        // the session's role set would let a later membership change rewrite history.
        var roles = new List<string> { RolePolicy.CompassOpsRole };
        var entry = CompassWrite(roles);

        // Act
        AuditLog log = await LogAndReadBackAsync(entry);
        roles.Add(RolePolicy.CompassSuperAdminRole);

        // Assert
        log.EffectiveRoles.ShouldNotBeNull();
        log.EffectiveRoles.ShouldBe([RolePolicy.CompassOpsRole]);
    }

    [Fact]
    public async Task LogAsync_OpsAndSuperAdmin_ProduceDistinguishableEntriesForTheSameChange()
    {
        // Arrange — the O5 requirement, and the single test that proves the contract does its job:
        // an identical change, made by two people whose capability is identical, must still record
        // which authority each acted under.
        var byOps = CompassWrite([RolePolicy.CompassOpsRole], actor: "33333333-3333-3333-3333-333333333333");
        var bySuperAdmin = CompassWrite([RolePolicy.CompassSuperAdminRole], actor: "44444444-4444-4444-4444-444444444444");

        // Act
        await _service.LogAsync(byOps);
        await _service.LogAsync(bySuperAdmin);

        // Assert
        var logs = (await _repository.GetAllAsync()).ToList();
        logs.Count.ShouldBe(2);
        logs[0].Action.ShouldBe(logs[1].Action);
        logs[0].EntityId.ShouldBe(logs[1].EntityId);
        logs[0].EffectiveRoles.ShouldNotBe(logs[1].EffectiveRoles);
    }

    [Fact]
    public async Task LogAsync_BulkWrite_IsAttributedToTheNamedMigrationPrincipal()
    {
        // Arrange — never an empty actor, never a fabricated user, never whichever operator happened
        // to trigger the job.
        var entry = new AuditEntry(
            EntityType: "CompassClientAssignment",
            EntityId: "legacy-batch-1",
            Action: "Create",
            Actor: AuditPrincipals.Migration,
            TriggeredBy: AuditPrincipals.Migration,
            Reason: "Legacy TPS assignment loaded during migration",
            Changes: [new FieldChange("Source", null, "Legacy Migrated")],
            EffectiveRoles: []);

        // Act
        AuditLog log = await LogAndReadBackAsync(entry);

        // Assert
        log.Actor.ShouldBe(AuditPrincipals.Migration);
        log.TriggeredBy.ShouldBe(AuditPrincipals.Migration);
    }

    [Fact]
    public async Task LogAsync_AUserHoldingBothOpsAndSuperAdmin_IsDistinguishableFromAnOpsOnlyWriter()
    {
        // Arrange — FR-067. The union is what the user actually held, so an entry must name BOTH
        // roles. This is a different question from the Ops-vs-SuperAdmin comparison below: here the
        // two writers overlap, and the Ops-only entry must not be mistaken for a subset of a
        // dual-role one when someone asks "who was acting as root when this changed?".
        var byBoth = CompassWrite(
            [RolePolicy.CompassOpsRole, RolePolicy.CompassSuperAdminRole],
            actor: "55555555-5555-5555-5555-555555555555");
        var byOpsOnly = CompassWrite(
            [RolePolicy.CompassOpsRole],
            actor: "66666666-6666-6666-6666-666666666666",
            entityId: "8");

        // Act
        await _service.LogAsync(byBoth);
        await _service.LogAsync(byOpsOnly);

        // Assert
        var logs = (await _repository.GetAllAsync()).ToList();
        AuditLog both = logs.Single(l => l.Actor.StartsWith('5'));
        AuditLog opsOnly = logs.Single(l => l.Actor.StartsWith('6'));

        both.EffectiveRoles.ShouldBe(
            [RolePolicy.CompassOpsRole, RolePolicy.CompassSuperAdminRole],
            ignoreOrder: true);
        opsOnly.EffectiveRoles.ShouldBe([RolePolicy.CompassOpsRole]);
        both.EffectiveRoles.ShouldNotBe(opsOnly.EffectiveRoles);
    }

    [Fact]
    public async Task LogAsync_RecordsTheWholePrivilegeSet_IncludingNonCompassRoles()
    {
        // Arrange — owner decision, 2026-08-10: audit_logs lives in the `public` schema and belongs to
        // the platform, so an entry records the authority the actor ACTUALLY held, not a Compass-shaped
        // subset. `ICurrentUserContext.Privileges` is the union of the user_roles table and the IdP
        // privilege claims, so a real session looks like this: Compass roles alongside Timesheet ones.
        //
        // This test exists to make narrowing FAIL. Filtering to a "Compass " prefix here would look
        // like a tidy-up — the frontend does exactly that in compass-nav-permissions.ts — but that
        // filter decides what to RENDER, not what to RECORD, and copying it into the write path would
        // quietly discard authority from the audit trail.
        var entry = CompassWrite([
            RolePolicy.CompassOpsRole,
            RolePolicy.Manager,
            RolePolicy.EDJEr,
        ]);

        // Act
        AuditLog log = await LogAndReadBackAsync(entry);

        // Assert
        log.EffectiveRoles.ShouldNotBeNull();
        log.EffectiveRoles.ShouldBe(
            [RolePolicy.CompassOpsRole, RolePolicy.EDJEr, RolePolicy.Manager],
            ignoreOrder: true);
        log.EffectiveRoles.ShouldContain(RolePolicy.Manager, "a non-Compass role must survive the write");
    }

    [Fact]
    public async Task LogAsync_TreatsCasingVariantsOfOneRoleAsOneRole()
    {
        // Arrange — CurrentUserContext.Privileges merges the user_roles table with IdP privilege
        // claims into a HashSet built with the DEFAULT case-sensitive comparer, so one user really can
        // arrive holding both spellings. Role comparison is case-INSENSITIVE everywhere else (see
        // KnownRoles), so these are one authority, and storing both would make the set query
        // `effective_roles @> ARRAY['Compass Ops']` depend on which spelling happened to be recorded.
        var entry = CompassWrite(["Compass Ops", "compass ops", "COMPASS OPS"]);

        // Act
        AuditLog log = await LogAndReadBackAsync(entry);

        // Assert
        log.EffectiveRoles.ShouldNotBeNull();
        log.EffectiveRoles.Count.ShouldBe(1);
    }

    [Fact]
    public async Task LogAsync_PicksTheSameRepresentative_RegardlessOfTheOrderCasingVariantsArriveIn()
    {
        // Arrange — the HashSet has no stable enumeration order, so de-duplication must not let the
        // stored spelling depend on which variant happened to come first.
        var first = CompassWrite(["compass ops", "Compass Ops"], entityId: "1");
        var second = CompassWrite(["Compass Ops", "compass ops"], entityId: "2");

        // Act
        await _service.LogAsync(first);
        await _service.LogAsync(second);

        // Assert
        var logs = (await _repository.GetAllAsync()).ToList();
        logs[0].EffectiveRoles.ShouldBe(logs[1].EffectiveRoles);
    }

    [Fact]
    public void DirectlyConstructedAuditLog_IsNormalisedToo()
    {
        // Arrange — a seeder or migration loader that builds the entity rather than going through
        // IAuditService must get the same guarantees, or the contract documented on the property is
        // true only of one code path. The setter is where the invariant lives, so this is that check.
        var roles = new List<string> { "Compass Super Admin", "compass ops", "Compass Ops" };

        // Act
        var log = new AuditLog { EffectiveRoles = roles };
        roles.Add(RolePolicy.CompassSalesRole);

        // Assert — copied (the later Add is not reflected), de-duplicated, and sorted.
        log.EffectiveRoles.ShouldNotBeNull();
        log.EffectiveRoles.Count.ShouldBe(2);
        log.EffectiveRoles[0].ShouldBe("Compass Ops");
        log.EffectiveRoles[1].ShouldBe(RolePolicy.CompassSuperAdminRole);
    }

    [Fact]
    public void ToResponse_SurfacesTheRecordedAuthority_SoItIsReadableThroughTheApi()
    {
        // Arrange — a field that is written but never projected is unreadable outside psql, which
        // defeats the point: when an Ops user and a Super Admin make the identical change, the audit
        // record is the ONLY thing that distinguishes them, so it has to reach whoever is looking.
        var log = new AuditLog
        {
            EntityType = "CompassClientAssignment",
            EntityId = "7",
            Action = "Update",
            Actor = "11111111-1111-1111-1111-111111111111",
            EffectiveRoles = [RolePolicy.CompassSuperAdminRole],
        };

        // Act
        AuditLogResponse response = log.ToResponse("Ada Lovelace");

        // Assert
        response.EffectiveRoles.ShouldBe([RolePolicy.CompassSuperAdminRole]);
    }

    [Fact]
    public void ToResponse_KeepsNotCapturedDistinctFromNoRolesHeld()
    {
        // Flattening null to an empty list on the way out would erase the distinction at the API
        // boundary even though the column holds it correctly — so the DTO stays nullable too.
        new AuditLog { EffectiveRoles = null }.ToResponse("x").EffectiveRoles.ShouldBeNull();
        new AuditLog { EffectiveRoles = [] }.ToResponse("x").EffectiveRoles.ShouldBeEmpty();
    }

    [Fact]
    public void DirectlyConstructedAuditLog_KeepsNullDistinctFromEmpty()
    {
        // The normalising setter must not "helpfully" turn null into an empty list.
        new AuditLog { EffectiveRoles = null }.EffectiveRoles.ShouldBeNull();
        new AuditLog { EffectiveRoles = [] }.EffectiveRoles.ShouldBeEmpty();
    }

    [Fact]
    public void MigrationPrincipal_CannotCollideWithAHumanActor()
    {
        // Every human actor is written as an EdjeId (see the `Actor => currentUser.EdjeId.ToString()`
        // convention across the admin services), so the system principal must not be parseable as one.
        // Otherwise "was this a person or the migration?" stops being answerable from the row.
        AuditPrincipals.Migration.ShouldNotBeNullOrWhiteSpace();
        Guid.TryParse(AuditPrincipals.Migration, out _).ShouldBeFalse();
    }
}
