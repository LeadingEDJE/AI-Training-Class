using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Services;

/// <summary>
/// ADR-007's retention asymmetry against real PostgreSQL — audit entries purge at two years,
/// business records never (issues #145, #86; AC-NFR-3, BR-16).
/// </summary>
/// <remarks>
/// <para>
/// #317 is resolved: the purge runs, and the append-only guarantee still stands. <c>audit_logs</c>
/// refuses every DELETE except one issued inside a transaction that has opted in via
/// <c>SET LOCAL leap.audit_retention_purge = 'on'</c>, which <c>AuditRetentionService</c> is the only
/// code to do. The first two tests below assert what is still IMPOSSIBLE, and they are the reason the
/// rest are safe: remove the opt-in check and the purge keeps passing while those two go red.
/// </para>
/// <para>
/// Integration rather than unit, because the whole point is a set-based DELETE. The purge runs
/// as SQL against the real provider; a change-tracker-based test would prove the arithmetic and nothing
/// about what is actually removed. The asymmetry is the requirement, so the tests that matter are the
/// ones asserting what SURVIVES.
/// </para>
/// <para>
/// The <c>notification_log</c> exemption is not incidental. Feature 006's coach-notification
/// contract made that table load-bearing for a business rule (Obligation O-1): "already notified" is
/// derived from it, so deleting a row RESURRECTS a notification — the next edit becomes eligible to
/// send a second coach email, silently violating AC-29/AC-32 and BR-6. It is a log by name, which is
/// exactly why someone will eventually reach for it, and why the exemption is asserted here rather than
/// left to the fact that the job happens not to mention it today.
/// </para>
/// </remarks>
public class AuditRetentionServiceTests(IntegrationTestFactory factory) : IntegrationTestBase(factory)
{
    private readonly IntegrationTestFactory _factory = factory;

    private const int EmployeeTypeId = 1;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A timestamp comfortably outside any plausible retention window.</summary>
    private static DateTime LongExpired => DateTime.UtcNow.AddYears(-5);

    // ---------------------------------------------------------------- the guarantee that SURVIVES

    /// <summary>
    /// An ORDINARY delete is still refused — the append-only guarantee is scoped, not removed (#317).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the test that makes the #317 resolution safe rather than convenient. The purge works
    /// because it opts in inside its own transaction; every other caller — an application code path, an
    /// ad-hoc <c>psql</c> session, a compromised endpoint — still hits the trigger. Delete the opt-in
    /// check from the migration and the purge keeps passing while this goes red.
    /// </para>
    /// <para>
    /// <c>AuditLogTriggerTests</c> asserts the same refusal through EF's change tracker and passes
    /// UNMODIFIED across this change, which is the other half of the evidence: the tampering guarantee
    /// it was written to protect is the one still standing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnOrdinaryDelete_IsStillRefused_ByTheAppendOnlyTrigger()
    {
        // Arrange
        await ResetDatabaseAsync();
        var id = await SeedAuditAsync(LongExpired);

        // Act — a delete with no purge opt-in, exactly as any other caller would issue it.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var delete = async () =>
            await db.AuditLogs.Where(entry => entry.Id == id).ExecuteDeleteAsync(Token);

        // Assert
        var error = await Should.ThrowAsync<Exception>(delete);
        FlattenMessages(error).ShouldContain("cannot be deleted", Case.Insensitive);
        (await AuditExistsAsync(id)).ShouldBeTrue("a refused delete must leave the row in place");
    }

    /// <summary>An UPDATE is refused unconditionally — the purge flag does not unlock edits.</summary>
    /// <remarks>
    /// The opt-in is scoped to DELETE alone. Nothing in this system ever legitimately edits an audit
    /// row, so there is no flag for it, and this asserts the migration did not widen the hole while
    /// making one.
    /// </remarks>
    [Fact]
    public async Task AnUpdate_IsRefused_EvenInsideAPurgeTransaction()
    {
        // Arrange
        await ResetDatabaseAsync();
        var id = await SeedAuditAsync(LongExpired);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act — opt in exactly as the purge does, then attempt an edit rather than a delete.
        var update = async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(Token);
            await db.Database.ExecuteSqlRawAsync(
                "SET LOCAL leap.audit_retention_purge = 'on'", Token);
            await db.AuditLogs
                .Where(entry => entry.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.Reason, "tampered"), Token);
            await transaction.CommitAsync(Token);
        };

        // Assert
        var error = await Should.ThrowAsync<Exception>(update);
        FlattenMessages(error).ShouldContain("cannot be modified", Case.Insensitive);
    }

    /// <summary>Walks the exception chain, since the provider wraps the Postgres error.</summary>
    private static string FlattenMessages(Exception error)
    {
        var messages = new List<string>();
        for (var current = error; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    // ---------------------------------------------------------------- the purge itself

    [Fact]
    public async Task PurgeAsync_RemovesAuditEntriesOlderThanTheRetentionWindow()
    {
        // Arrange
        await ResetDatabaseAsync();
        var expired = await SeedAuditAsync(LongExpired);

        // Act
        var purged = await RunPurgeAsync();

        // Assert
        purged.ShouldBeGreaterThan(0, "the job reports what it removed, for the operator's log");
        (await AuditExistsAsync(expired)).ShouldBeFalse();
    }

    [Fact]
    public async Task PurgeAsync_KeepsAuditEntriesInsideTheWindow()
    {
        // Arrange
        await ResetDatabaseAsync();
        var recent = await SeedAuditAsync(DateTime.UtcNow.AddDays(-30));

        // Act
        await RunPurgeAsync();

        // Assert
        (await AuditExistsAsync(recent)).ShouldBeTrue();
    }

    /// <summary>The boundary: the window is inclusive, so a row exactly at the cutoff is KEPT.</summary>
    /// <remarks>
    /// "Retained for 2 years, then purged" gives the row its full second year. Off by one day here is
    /// invisible in casual use and deletes a day of history nobody asked to lose, which is the kind of
    /// thing only an explicit boundary test catches.
    /// </remarks>
    [Fact]
    public async Task PurgeAsync_TreatsTheRetentionBoundaryAsInclusive()
    {
        // Arrange — one row a hair inside the boundary, one a hair outside.
        await ResetDatabaseAsync();
        var justInside = await SeedAuditAsync(DateTime.UtcNow.AddYears(-2).AddHours(1));
        var justOutside = await SeedAuditAsync(DateTime.UtcNow.AddYears(-2).AddHours(-1));

        // Act
        await RunPurgeAsync();

        // Assert
        (await AuditExistsAsync(justInside)).ShouldBeTrue("a row inside the window is retained");
        (await AuditExistsAsync(justOutside)).ShouldBeFalse("a row past the window is purged");
    }

    [Fact]
    public async Task PurgeAsync_OnAnEmptyTable_IsAHarmlessNoOp()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var purged = await RunPurgeAsync();

        // Assert
        purged.ShouldBe(0);
    }

    [Fact]
    public async Task PurgeAsync_IsIdempotent()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAuditAsync(LongExpired);

        // Act
        var first = await RunPurgeAsync();
        var second = await RunPurgeAsync();

        // Assert
        first.ShouldBeGreaterThan(0);
        second.ShouldBe(0, "a second pass has nothing left to do");
    }

    // ---------------------------------------------------------------- what must SURVIVE

    /// <summary>
    /// BR-16 — assignment, SOW, client and EDJEr records older than the window are NEVER purged.
    /// </summary>
    /// <remarks>
    /// The other half of ADR-007, and the half whose failure would be silent: a purge that took business
    /// data with it would leave the application working and the history quietly wrong.
    /// </remarks>
    [Fact]
    public async Task PurgeAsync_NeverTouchesBusinessRecords_HoweverOldTheyAre()
    {
        // Arrange — a full chain, all of it far outside the retention window.
        await ResetDatabaseAsync();
        var seeded = await SeedAgedBusinessChainAsync();
        await SeedAuditAsync(LongExpired);

        // Act
        await RunPurgeAsync();

        // Assert
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<Employee>().AnyAsync(e => e.Id == seeded.EmployeeId, Token))
            .ShouldBeTrue("an EDJEr is a business record (BR-16)");
        (await db.Set<Client>().AnyAsync(c => c.Id == seeded.ClientId, Token))
            .ShouldBeTrue("a client is a business record (BR-16)");
        (await db.Set<ClientAssignment>().AnyAsync(a => a.Id == seeded.AssignmentId, Token))
            .ShouldBeTrue("an assignment is a business record (BR-16)");
        (await db.Set<Sow>().AnyAsync(s => s.Id == seeded.SowId, Token))
            .ShouldBeTrue("a SOW is a business record (BR-16)");
    }

    /// <summary>
    /// ADR-007's own named test: an aged assignment still appears in the AC-24 history after the purge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ADR asks for this against AC-24 and AC-39. This is the AC-24 half; the AC-39 half is
    /// the test below, added once #77 / PR #311 landed the Client Assignment Duration report. Both are
    /// needed: they fail differently, because one reads a client's history and the other sums a pair's
    /// tenure across it.
    /// </para>
    /// <para>
    /// This asserts through the real read path rather than a row count, because "the row still exists"
    /// and "the history still reports it" are different claims, and it is the second one AC-24 makes.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task PurgeAsync_LeavesAnAgedAssignmentVisibleInTheAc24History()
    {
        // Arrange
        await ResetDatabaseAsync();
        var seeded = await SeedAgedBusinessChainAsync();
        await SeedAuditAsync(LongExpired);

        // Act
        await RunPurgeAsync();

        // Assert — through the client view, which is what AC-24 is about.
        var response = await _factory.AsCompassOps()
            .GetAsync($"/api/compass/client-directory/{seeded.ClientId}", Token);
        response.EnsureSuccessStatusCode();
        var view = await response.Content
            .ReadFromJsonAsync<Api.Modules.Compass.Dtos.Read.ClientViewDto>(Token);

        view!.AssignmentHistory.ShouldNotBeEmpty(
            "AC-24's history is never truncated by retention policy");
        view.AssignmentHistory.ShouldContain(row => row.AssignmentId == seeded.AssignmentId);
    }

    /// <summary>
    /// ADR-007's named test, AC-39 half: an aged assignment still counts toward reported tenure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the criterion a retention bug would corrupt most quietly. AC-24 would at least go
    /// visibly short — a row missing from a history anyone can look at. AC-39 reports a NUMBER, summed
    /// across every assignment for an EDJEr/client pair; drop the old ones and the report still renders,
    /// still looks plausible, and is simply wrong. FR-015 says the sum must include ended assignments
    /// and must not be truncated by any retention policy, "because retention can only ever remove older
    /// ended rows, which are exactly the rows this sum depends on".
    /// </para>
    /// <para>
    /// The assertion is on <c>TotalDays</c> rather than merely on the row's presence: a purge that took
    /// the assignment would leave the pair unreported entirely OR reported at a smaller total, and only
    /// the number distinguishes "still counted" from "still listed".
    /// </para>
    /// </remarks>
    [Fact]
    public async Task PurgeAsync_LeavesAnAgedAssignmentCountedInTheAc39Duration()
    {
        // Arrange — FR-015's re-engagement case, and the report's population rule makes it the ONLY
        // shape that can express this guarantee. The duration report covers pairs holding at least one
        // ACTIVE assignment (FR-032), so a pair whose every assignment ended is absent by design rather
        // than by retention. The aged ENDED assignment therefore needs a CURRENT one beside it: the
        // EDJEr who worked a client, left, and returned. That is exactly FR-015's "combined tenure",
        // and the old row is precisely what a purge would take.
        await ResetDatabaseAsync();
        var seeded = await SeedAgedBusinessChainAsync();
        var currentSpanDays = await SeedCurrentAssignmentAsync(seeded.EmployeeId, seeded.ClientId);
        await SeedAuditAsync(LongExpired);

        var before = await DurationForAsync(seeded.EmployeeId, seeded.ClientId);
        before.ShouldNotBeNull("the pair must be reported before the purge, or this proves nothing");
        before.TotalDays.ShouldBeGreaterThan(
            currentSpanDays,
            "the aged ended assignment must already be counted, or the purge has nothing to lose");

        // Act
        await RunPurgeAsync();

        // Assert
        var after = await DurationForAsync(seeded.EmployeeId, seeded.ClientId);
        after.ShouldNotBeNull("AC-39 is never truncated by retention policy (FR-015, BR-16)");
        after.TotalDays.ShouldBe(before.TotalDays, "the purge must not shorten reported tenure");
        after.TotalDays.ShouldBeGreaterThan(
            currentSpanDays, "the aged assignment must STILL be counted after the purge");
    }

    /// <summary>
    /// Adds a CURRENT assignment for an existing pair and returns its span in whole days.
    /// </summary>
    /// <remarks>
    /// Kept out of <see cref="SeedAgedBusinessChainAsync"/> so the other tests keep the minimal fixture
    /// they were written against: only the AC-39 report needs the pair to qualify as active.
    /// </remarks>
    private async Task<int> SeedCurrentAssignmentAsync(int employeeId, int clientId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);
        db.Set<ClientAssignment>().Add(new ClientAssignment
        {
            EmployeeId = employeeId,
            ClientId = clientId,
            StartDate = start,
            EndDate = null,
        });
        await db.SaveChangesAsync(Token);

        // Inclusive of both ends, matching the report's own span rule (FR-015).
        return 31;
    }

    /// <summary>The AC-39 row for one EDJEr/client pair, or null when the report omits the pair.</summary>
    private async Task<AssignmentDurationRowDto?> DurationForAsync(int employeeId, int clientId)
    {
        var response = await _factory.AsCompassOps()
            .GetAsync("/api/compass/reports/assignment-duration", Token);
        response.EnsureSuccessStatusCode();

        var rows = await response.Content
            .ReadFromJsonAsync<List<AssignmentDurationRowDto>>(Token);

        return rows!.SingleOrDefault(row => row.EmployeeId == employeeId && row.ClientId == clientId);
    }

    /// <summary>
    /// Obligation O-1 — <c>notification_log</c> rows are NOT purged, however old.
    /// </summary>
    /// <remarks>
    /// Deleting one resurrects a notification: "already notified" is derived from this table, so the next
    /// edit becomes eligible to send a second coach email (AC-29/AC-32, BR-6). The row is the only record
    /// that the first email happened.
    /// </remarks>
    [Fact]
    public async Task PurgeAsync_NeverPrunesTheNotificationLog()
    {
        // Arrange
        await ResetDatabaseAsync();
        var notificationId = await SeedNotificationAsync(LongExpired);

        // Act
        await RunPurgeAsync();

        // Assert
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.NotificationLogs.AnyAsync(n => n.Id == notificationId, Token))
            .ShouldBeTrue("pruning this row would make a second coach email deliverable (O-1)");
    }

    // ---------------------------------------------------------------- helpers

    private async Task<int> RunPurgeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuditRetentionService>();
        return await service.PurgeExpiredAuditEntriesAsync(Token);
    }

    private async Task<long> SeedAuditAsync(DateTime timestamp)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var entry = new AuditLog
        {
            EntityType = "ClientAssignment",
            EntityId = "1",
            Action = "update",
            Actor = "tester",
            TriggeredBy = "UI",
            Reason = "seeded for a retention test",
            Changes = "[]",
            Timestamp = timestamp,
        };
        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync(Token);
        return entry.Id;
    }

    private async Task<bool> AuditExistsAsync(long id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        return await db.AuditLogs.AnyAsync(a => a.Id == id, Token);
    }

    private async Task<long> SeedNotificationAsync(DateTime createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var log = new NotificationLog
        {
            EmployeeId = "9",
            NotificationType = "Compass.AssignmentEnded:1",
            PeriodWeekStart = DateOnly.FromDateTime(createdAt),
            Channel = "email",
            Status = "Sent",
            RecipientEmail = "coach@example.test",
            CreatedAt = createdAt,
        };
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync(Token);
        return log.Id;
    }

    private async Task<(int EmployeeId, int ClientId, int AssignmentId, int SowId)>
        SeedAgedBusinessChainAsync()
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
            HireDate = new DateOnly(2015, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
        };
        var client = new Client { ClientName = $"Acme-{Guid.NewGuid():N}", IsInternal = false };
        db.Set<Employee>().Add(employee);
        db.Set<Client>().Add(client);
        await db.SaveChangesAsync(Token);

        // An assignment that started and ENDED long before the retention window — the shape AC-24 and
        // AC-39 exist to keep reporting on.
        var assignment = new ClientAssignment
        {
            EmployeeId = employee.Id,
            ClientId = client.Id,
            StartDate = new DateOnly(2016, 1, 1),
            EndDate = new DateOnly(2017, 12, 31),
        };
        db.Set<ClientAssignment>().Add(assignment);
        await db.SaveChangesAsync(Token);

        var sow = new Sow
        {
            ClientAssignmentId = assignment.Id,
            SowType = SowType.InitialContract,
            RateIncrease = false,
            SowStartDate = new DateOnly(2016, 1, 1),
            SowEndDate = new DateOnly(2017, 12, 31),
            HasPassedApplicationValidation = true,
        };
        db.Set<Sow>().Add(sow);
        await db.SaveChangesAsync(Token);

        return (employee.Id, client.Id, assignment.Id, sow.Id);
    }
}
