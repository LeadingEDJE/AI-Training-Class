using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// The Compass data clear: what it reports, what it records, and in what order it does them.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written in-memory doubles rather than a mocking framework, matching
/// <c>CompassEmployeeServiceTests</c> and the project's conventions.
/// </para>
/// <para>
/// What this file cannot establish. The repository behind the double issues a real Postgres
/// <c>TRUNCATE</c>. That it parses, that naming exactly five tables satisfies every foreign key
/// between them, that identity sequences restart, and that the two Compass lookup tables survive are
/// all asserted against real PostgreSQL in
/// <c>tests/integration/Endpoints/CompassDeveloperToolsEndpointsTests.cs</c>. The statement's TEXT is
/// asserted without a database in <c>CompassDataResetRepositoryTests</c>.
/// </para>
/// </remarks>
public class CompassDataResetServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly DateTime ClearedAt = new(2026, 8, 26, 14, 30, 0, DateTimeKind.Utc);

    /// <summary>
    /// Counts how many times the clear was issued, and hands back fixed numbers.
    /// </summary>
    /// <remarks>
    /// It no longer records call ORDER, because order stopped being this layer's problem. An
    /// earlier revision had the repository expose <c>CountAsync</c> and <c>ClearAsync</c> separately
    /// and this double asserted the service called them in that order — which was true, and not
    /// enough: between the two calls a concurrent insert was destroyed without being counted, so the
    /// audit entry understated what was lost. The repository now counts under the truncate's own lock
    /// and returns the numbers, so the service CANNOT get one without the other and there is no
    /// ordering left to get wrong. Review found that gap (PR #452); the fix was structural, so the
    /// test that guarded it is gone rather than weakened.
    /// </remarks>
    private sealed class FakeResetRepository : ICompassDataResetRepository
    {
        public int ClearCalls { get; private set; }

        public CompassTableRowCounts Counts { get; init; } = new(3, 4, 5, 97, 28);

        public Task<CompassTableRowCounts> ClearAsync(CancellationToken cancellationToken)
        {
            ClearCalls += 1;
            return Task.FromResult(Counts);
        }
    }

    private sealed class RecordingAuditService : IAuditService
    {
        public List<AuditEntry> Entries { get; } = [];

        /// <summary>When set, <see cref="LogAsync"/> throws it — the audit-write-failed path.</summary>
        public DbUpdateException? ThrowOnLog { get; init; }

        public Task LogAsync(AuditEntry entry)
        {
            if (ThrowOnLog is not null)
            {
                throw ThrowOnLog;
            }

            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditLogResponse>> GetByEntityAsync(string entityType, string entityId) =>
            throw new NotSupportedException("The Compass clear never reads the audit trail");

        public Task<PaginatedAuditLogResponse> BrowseAsync(
            string? entityType,
            string? actor,
            string? employeeId,
            DateTime? fromDate,
            DateTime? toDate,
            int page,
            int pageSize
        ) => throw new NotSupportedException("The Compass clear never reads the audit trail");

        public Task<IReadOnlyList<string>> GetDistinctEntityTypesAsync() =>
            throw new NotSupportedException("The Compass clear never reads the audit trail");
    }

    private sealed class StubCurrentUser : ICurrentUserContext
    {
        public Guid EdjeId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public string Email => "compass.root@example.test";
        public string TpsEmployeeId => "1";
        public IReadOnlyList<string> Privileges => ["Compass Super Admin"];
        public bool HasPrivilege(string privilege) => Privileges.Contains(privilege);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(ClearedAt, TimeSpan.Zero);
    }

    private static (CompassDataResetService Service, FakeResetRepository Repository, RecordingAuditService Audit) Build(
        CompassTableRowCounts? counts = null,
        DbUpdateException? auditFailure = null)
    {
        var repository = new FakeResetRepository { Counts = counts ?? new(3, 4, 5, 97, 28) };
        var audit = new RecordingAuditService { ThrowOnLog = auditFailure };
        var service = new CompassDataResetService(
            repository,
            audit,
            new StubCurrentUser(),
            new FixedClock(),
            NullLogger<CompassDataResetService>.Instance);
        return (service, repository, audit);
    }

    [Fact]
    public async Task ClearAllAsync_ReportsThePerTableCountsAndTheirSum()
    {
        // Arrange
        var (service, _, _) = Build();

        // Act
        var result = await service.ClearAllAsync(Token);

        // Assert — the total is derived, never separately counted, so it cannot disagree with the parts.
        result.BillableTimeCategories.ShouldBe(3);
        result.Sows.ShouldBe(4);
        result.ClientAssignments.ShouldBe(5);
        result.Employees.ShouldBe(97);
        result.Clients.ShouldBe(28);
        result.TotalRowsCleared.ShouldBe(137);
        result.ClearedAtUtc.ShouldBe(ClearedAt);
    }

    [Fact]
    public async Task ClearAllAsync_IssuesExactlyOneClear()
    {
        // Arrange — the service must not retry, and must not clear twice to obtain the numbers. The
        // second clear would report zeros against an already-empty database and overwrite the only
        // honest answer the caller ever gets.
        var (service, repository, _) = Build();

        // Act
        await service.ClearAllAsync(Token);

        // Assert
        repository.ClearCalls.ShouldBe(1);
    }

    [Fact]
    public async Task ClearAllAsync_ReportsTheRepositoryCountsRatherThanCountingAnything()
    {
        // Arrange — deliberately lopsided numbers no accidental recomputation would reproduce.
        var (service, _, audit) = Build(counts: new(1, 2, 3, 4, 5));

        // Act
        var result = await service.ClearAllAsync(Token);

        // Assert — the numbers the repository measured under its lock are what the caller AND the
        // audit trail see. A service that re-counted would see an already-empty database and report
        // zeros to both.
        result.TotalRowsCleared.ShouldBe(15);
        audit.Entries.ShouldHaveSingleItem()
            .Changes.Single(change => change.Field == "total").Before.ShouldBe("15");
    }

    [Fact]
    public async Task ClearAllAsync_OnAnEmptyDatabase_ReportsZeroAndStillClears()
    {
        // Arrange — pressing the button twice must not be an error. The second press is the realistic
        // one: someone clears, sees the message, and clicks again to be sure.
        var (service, repository, _) = Build(counts: new(0, 0, 0, 0, 0));

        // Act
        var result = await service.ClearAllAsync(Token);

        // Assert
        result.TotalRowsCleared.ShouldBe(0);
        repository.ClearCalls.ShouldBe(1);
    }

    [Fact]
    public async Task ClearAllAsync_RecordsOneAuditEntryNamingTheActorAndTheCounts()
    {
        // Arrange
        var (service, _, audit) = Build();

        // Act
        await service.ClearAllAsync(Token);

        // Assert — the audit record is the only surviving evidence of what was destroyed, so it has to
        // carry the numbers and not merely the fact that a button was pressed.
        var entry = audit.Entries.ShouldHaveSingleItem();
        entry.EntityType.ShouldBe("CompassData");
        entry.EntityId.ShouldBe("compass");
        entry.Action.ShouldBe("clear");
        entry.Actor.ShouldBe("22222222-2222-2222-2222-222222222222");
        entry.TriggeredBy.ShouldBe("DeveloperTools");
        entry.EffectiveRoles.ShouldBe(["Compass Super Admin"]);

        var employees = entry.Changes.Single(change => change.Field == "employee");
        employees.Before.ShouldBe("97");
        employees.After.ShouldBe("0");

        var total = entry.Changes.Single(change => change.Field == "total");
        total.Before.ShouldBe("137");
        total.After.ShouldBe("0");
    }

    [Fact]
    public async Task ClearAllAsync_RecordsNoLookupTableInTheAuditEntry()
    {
        // Arrange — the audit record must not claim to have cleared reference data it never touches.
        var (service, _, audit) = Build();

        // Act
        await service.ClearAllAsync(Token);

        // Assert
        var fields = audit.Entries.ShouldHaveSingleItem().Changes.Select(change => change.Field).ToList();
        fields.ShouldNotContain("employee_type");
        fields.ShouldNotContain("invoice_frequency_type");
    }

    [Fact]
    public async Task ClearAllAsync_WhenTheAuditWriteFails_StillReportsTheClear()
    {
        // Arrange — by the time the audit is written the rows are already gone and committed. Failing
        // the request here would tell the caller the clear did not happen, which is the one thing that
        // is definitely untrue, and would invite a second press against an already-empty database.
        var (service, repository, _) = Build(auditFailure: new DbUpdateException("audit table is gone"));

        // Act
        var result = await service.ClearAllAsync(Token);

        // Assert
        result.TotalRowsCleared.ShouldBe(137);
        repository.ClearCalls.ShouldBe(1);
    }
}
