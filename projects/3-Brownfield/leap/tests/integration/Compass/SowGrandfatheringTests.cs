using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// US8/#67 — <see cref="SowType.LegacyMigrated"/> grandfathering: exempt at LOAD, fully validated on
/// its FIRST application edit, and never exempt again (AC-41, BR-14, FR-046, FR-046a, J17a).
/// </summary>
/// <remarks>
/// <para>
/// The load exemption is a database fact, not application code. The two SOW constraints are
/// PARTIAL — <c>ck_sow_end_on_or_after_start</c> reads
/// <c>sow_type = 'LegacyMigrated' OR sow_end_date &gt;= sow_start_date</c>, and the non-overlap index
/// carries <c>WHERE (sow_type &lt;&gt; 'LegacyMigrated')</c>. That scoping IS AC-41's exemption
/// (data-model §3), which is why the first test here inserts rows the application would refuse and
/// asserts the database accepts them. Do not "tidy" those constraints into unconditional ones; doing so
/// would make a migrated row unloadable and this suite would say so.
/// </para>
/// <para>
/// What is deliberately NOT tested here: bypass containment and the migration principal.
/// FR-047 and FR-048 are withdrawn from this feature to Stream 6, which owns the loads (spec Q3,
/// Deviation 5). The issue text for #67 asks for "reachable only by the migration principal"; that half
/// is not deliverable here, and QA checkpoint #91's fourth bullet still demands it. Recorded so the gap
/// is visible rather than assumed covered.
/// </para>
/// </remarks>
public class SowGrandfatheringTests(IntegrationTestFactory factory) : IntegrationTestBase(factory)
{
    private readonly IntegrationTestFactory _factory = factory;

    private const int EmployeeTypeId = 1;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string SowsUrl(int assignmentId) => $"/api/compass/assignments/{assignmentId}/sows";

    // ---------------------------------------------------------------- T130 — the load exemption

    /// <summary>
    /// T130 — two OVERLAPPING legacy periods, one of them ending before it starts, both load.
    /// </summary>
    /// <remarks>
    /// Neither row could be written through the application: the overlap violates FR-019 and the
    /// inverted period violates FR-020. Both are inserted directly, exactly as a migration would, and
    /// the assertion is simply that the database took them — which is what "exempt at load" means and
    /// the only thing that proves the constraints are still partial.
    /// </remarks>
    [Fact]
    public async Task LegacyMigratedPeriods_ThatViolateBothRules_StillLoad()
    {
        // Arrange
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();

        // Act — a migration-shaped insert, bypassing the application entirely.
        var overlappingId = await SeedLegacySowAsync(
            assignmentId, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        var alsoOverlappingId = await SeedLegacySowAsync(
            assignmentId, new DateOnly(2024, 6, 1), new DateOnly(2025, 6, 30));
        var invertedId = await SeedLegacySowAsync(
            assignmentId, new DateOnly(2024, 9, 1), new DateOnly(2024, 3, 1));

        // Assert
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var stored = await db.Set<Sow>().AsNoTracking()
            .Where(s => s.ClientAssignmentId == assignmentId)
            .ToListAsync(Token);

        stored.Count.ShouldBe(3, "the partial constraints exempt LegacyMigrated rows (AC-41)");
        stored.Select(s => s.Id).ShouldBe([overlappingId, alsoOverlappingId, invertedId], ignoreOrder: true);
        stored.ShouldAllBe(s => s.HasPassedApplicationValidation == false);
    }

    // ---------------------------------------------------------------- T131 — rejected until valid

    /// <summary>
    /// T131 — the first application edit of a legacy period is REJECTED while it still overlaps.
    /// </summary>
    [Fact]
    public async Task FirstEdit_IsRejected_WhileThePeriodStillOverlapsAnother()
    {
        // Arrange — two legacy periods that overlap each other.
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var target = await SeedLegacySowAsync(
            assignmentId, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        await SeedLegacySowAsync(assignmentId, new DateOnly(2024, 6, 1), new DateOnly(2025, 6, 30));

        // Act — an edit that changes the note but leaves the overlap in place.
        var response = await _factory.AsCompassOps().PutAsJsonAsync(
            $"{SowsUrl(assignmentId)}/{target}",
            new UpdateSowRequest(
                SowType.LegacyMigrated, RateIncrease: false,
                new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), "tidying the note"),
            Token);

        // Assert — FR-019 must be satisfied IN FULL before the first edit is accepted (FR-046).
        // 409 rather than 400: the contract gives an overlap its own status because the response also
        // identifies the conflicting period (sow-write-surface.md §5).
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await StoredAsync(target)).HasPassedApplicationValidation
            .ShouldBeFalse("a refused edit must not mark the period validated");
    }

    /// <summary>T131 — likewise rejected while the period ends before it starts (FR-020).</summary>
    [Fact]
    public async Task FirstEdit_IsRejected_WhileThePeriodEndsBeforeItStarts()
    {
        // Arrange
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var target = await SeedLegacySowAsync(
            assignmentId, new DateOnly(2024, 9, 1), new DateOnly(2024, 3, 1));

        // Act — resubmitting the stored, invalid dates.
        var response = await _factory.AsCompassOps().PutAsJsonAsync(
            $"{SowsUrl(assignmentId)}/{target}",
            new UpdateSowRequest(
                SowType.LegacyMigrated, RateIncrease: false,
                new DateOnly(2024, 9, 1), new DateOnly(2024, 3, 1), null),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StoredAsync(target)).HasPassedApplicationValidation.ShouldBeFalse();
    }

    // ---------------------------------------------------------------- T132 — corrected edit accepted

    /// <summary>
    /// T132 — the corrected edit is ACCEPTED and the period is thereafter recorded as validated.
    /// </summary>
    /// <remarks>
    /// The type stays <see cref="SowType.LegacyMigrated"/>. FR-046a describes the validated row
    /// as "a <c>Legacy Migrated</c> SOW … subject to FR-019 and FR-020 on every later edit", so
    /// validation flips <see cref="Sow.HasPassedApplicationValidation"/> and nothing else. Re-typing the
    /// row would erase the fact that it came from the legacy system, which nothing asks for.
    /// </remarks>
    [Fact]
    public async Task CorrectedFirstEdit_IsAccepted_AndMarksThePeriodValidated()
    {
        // Arrange
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var target = await SeedLegacySowAsync(
            assignmentId, new DateOnly(2024, 9, 1), new DateOnly(2024, 3, 1));

        // Act — the dates are corrected so FR-020 is satisfied in full.
        var response = await _factory.AsCompassOps().PutAsJsonAsync(
            $"{SowsUrl(assignmentId)}/{target}",
            new UpdateSowRequest(
                SowType.LegacyMigrated, RateIncrease: false,
                new DateOnly(2024, 3, 1), new DateOnly(2024, 9, 1), "corrected"),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = await StoredAsync(target);
        stored.HasPassedApplicationValidation.ShouldBeTrue();
        stored.SowType.ShouldBe(
            SowType.LegacyMigrated, "validation records a fact about the row, it does not re-type it");
        stored.SowStartDate.ShouldBe(new DateOnly(2024, 3, 1));
        stored.SowEndDate.ShouldBe(new DateOnly(2024, 9, 1));
    }

    // ---------------------------------------------------------------- T133 — no ongoing exemption

    /// <summary>
    /// T133 — once validated, a legacy period is subject to the same rules on every LATER edit.
    /// </summary>
    /// <remarks>
    /// FR-046a's whole point: the exemption is spent on first use and does not return. Without this the
    /// grandfathering would be a permanent licence attached to a type, which is exactly what BR-14 is
    /// written to prevent.
    /// </remarks>
    [Fact]
    public async Task AfterValidation_ALaterInvalidEdit_IsRejectedLikeAnyOtherPeriod()
    {
        // Arrange — validate it once.
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var target = await SeedLegacySowAsync(
            assignmentId, new DateOnly(2024, 9, 1), new DateOnly(2024, 3, 1));
        var httpClient = _factory.AsCompassOps();

        var firstEdit = await httpClient.PutAsJsonAsync(
            $"{SowsUrl(assignmentId)}/{target}",
            new UpdateSowRequest(
                SowType.LegacyMigrated, RateIncrease: false,
                new DateOnly(2024, 3, 1), new DateOnly(2024, 9, 1), "corrected"),
            Token);
        firstEdit.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Act — a later edit that reintroduces the inverted period.
        var response = await httpClient.PutAsJsonAsync(
            $"{SowsUrl(assignmentId)}/{target}",
            new UpdateSowRequest(
                SowType.LegacyMigrated, RateIncrease: false,
                new DateOnly(2024, 9, 1), new DateOnly(2024, 3, 1), "undoing the correction"),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var stored = await StoredAsync(target);
        stored.HasPassedApplicationValidation.ShouldBeTrue("validation is not undone by a refused edit");
        stored.SowStartDate.ShouldBe(new DateOnly(2024, 3, 1), "the refused edit changed nothing");
    }

    /// <summary>
    /// T133 — a period Compass itself created is subject to the same rules (US8 scenario 3).
    /// </summary>
    [Fact]
    public async Task APeriodCompassCreated_IsSubjectToTheSameRules()
    {
        // Arrange
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var httpClient = _factory.AsCompassOps();
        var created = await httpClient.PostAsJsonAsync(
            SowsUrl(assignmentId),
            new CreateSowRequest(
                SowType.InitialContract, RateIncrease: false,
                new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31), null),
            Token);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var row = await created.Content.ReadFromJsonAsync<SowRowDto>(Token);

        // Act — invert its dates.
        var response = await httpClient.PutAsJsonAsync(
            $"{SowsUrl(assignmentId)}/{row!.Id}",
            new UpdateSowRequest(
                SowType.InitialContract, RateIncrease: false,
                new DateOnly(2024, 12, 31), new DateOnly(2024, 4, 1), null),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- FR-016 — no laundering

    /// <summary>
    /// A normal period may NOT be converted INTO a legacy one, even though a legacy one may be edited.
    /// </summary>
    /// <remarks>
    /// The half of FR-016 that must survive #67. Opening the first-edit path is what AC-41 requires;
    /// letting any row claim the type would turn grandfathering into a general-purpose way around SOW
    /// validation, which is precisely what the withdrawn FR-047 was meant to prevent and what this
    /// assertion holds in its absence.
    /// </remarks>
    [Fact]
    public async Task ANormalPeriod_CannotBeConvertedIntoALegacyOne()
    {
        // Arrange
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var httpClient = _factory.AsCompassOps();
        var created = await httpClient.PostAsJsonAsync(
            SowsUrl(assignmentId),
            new CreateSowRequest(
                SowType.InitialContract, RateIncrease: false,
                new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31), null),
            Token);
        var row = await created.Content.ReadFromJsonAsync<SowRowDto>(Token);

        // Act
        var response = await httpClient.PutAsJsonAsync(
            $"{SowsUrl(assignmentId)}/{row!.Id}",
            new UpdateSowRequest(
                SowType.LegacyMigrated, RateIncrease: false,
                new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31), null),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StoredAsync(row.Id)).SowType.ShouldBe(SowType.InitialContract);
    }

    /// <summary>A legacy period still cannot be CREATED through the application (FR-016).</summary>
    [Fact]
    public async Task ALegacyPeriod_CannotBeCreatedThroughTheApplication()
    {
        // Arrange
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();

        // Act
        var response = await _factory.AsCompassOps().PostAsJsonAsync(
            SowsUrl(assignmentId),
            new CreateSowRequest(
                SowType.LegacyMigrated, RateIncrease: false,
                new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31), null),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- T134 — audited like any write

    /// <summary>
    /// T134 — the first edit of a legacy period is audited with effective roles, like every other write.
    /// </summary>
    [Fact]
    public async Task TheValidatingEdit_IsAudited_WithEffectiveRolesPopulated()
    {
        // Arrange
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var target = await SeedLegacySowAsync(
            assignmentId, new DateOnly(2024, 9, 1), new DateOnly(2024, 3, 1));

        // Act
        var response = await _factory.AsCompassOps().PutAsJsonAsync(
            $"{SowsUrl(assignmentId)}/{target}",
            new UpdateSowRequest(
                SowType.LegacyMigrated, RateIncrease: false,
                new DateOnly(2024, 3, 1), new DateOnly(2024, 9, 1), "corrected"),
            Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var audit = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "CompassSow" && a.Action == "update")
            .SingleAsync(Token);

        audit.EntityId.ShouldBe(target.ToString());
        audit.Reason.ShouldNotBeNullOrWhiteSpace();
        audit.EffectiveRoles.ShouldNotBeNull();
        audit.EffectiveRoles.ShouldNotBeEmpty(
            "a grandfathered edit is attributable like any other (AC-NFR-3, FR-049)");
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Sow> StoredAsync(int sowId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        return await db.Set<Sow>().AsNoTracking().SingleAsync(s => s.Id == sowId, Token);
    }

    /// <summary>
    /// Inserts a <see cref="SowType.LegacyMigrated"/> period DIRECTLY, the way a migration would.
    /// </summary>
    /// <remarks>
    /// Deliberately not through the API: the application refuses to create one (FR-016), and a row the
    /// application could have written would not be a legacy row in the sense AC-41 means. The partial
    /// constraints are what allow these inserts to succeed at all.
    /// </remarks>
    private async Task<int> SeedLegacySowAsync(int assignmentId, DateOnly start, DateOnly end)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var sow = new Sow
        {
            ClientAssignmentId = assignmentId,
            SowType = SowType.LegacyMigrated,
            RateIncrease = false,
            SowStartDate = start,
            SowEndDate = end,
            HasPassedApplicationValidation = false,
        };
        db.Set<Sow>().Add(sow);
        await db.SaveChangesAsync(Token);
        return sow.Id;
    }

    private async Task<int> SeedAssignmentAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        db.Set<EmployeeType>().Add(
            new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"ada-{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
        };
        var client = new Client { ClientName = $"Acme-{Guid.NewGuid():N}", IsInternal = false };
        db.Set<Employee>().Add(employee);
        db.Set<Client>().Add(client);
        await db.SaveChangesAsync(Token);

        var assignment = new ClientAssignment
        {
            EmployeeId = employee.Id,
            ClientId = client.Id,
            StartDate = new DateOnly(2024, 1, 1),
        };
        db.Set<ClientAssignment>().Add(assignment);
        await db.SaveChangesAsync(Token);

        return assignment.Id;
    }
}
