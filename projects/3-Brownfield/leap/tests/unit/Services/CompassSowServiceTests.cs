using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// US3/#66 — the SOW service's rejection paths (FR-016, FR-017, FR-019, FR-020), the
/// <c>HasPassedApplicationValidation</c> flip (FR-024), and the research R-4 exception translation.
/// </summary>
/// <remarks>
/// Hand-written in-memory doubles rather than a mocking framework, matching
/// <see cref="CompassAssignmentServiceTests"/> and the project's test conventions.
/// </remarks>
public class CompassSowServiceTests
{
    private const int AssignmentId = 1;
    private static readonly DateOnly Start = new(2024, 4, 1);
    private static readonly DateOnly End = new(2024, 12, 31);

    private sealed class FakeSowRepository : ICompassSowRepository
    {
        private readonly List<Sow> _rows = [];
        private readonly List<Sow> _pending = [];
        private int _nextId = 1;

        public Sow Seed(int clientAssignmentId, DateOnly start, DateOnly end, SowType type = SowType.InitialContract)
        {
            var sow = new Sow
            {
                Id = _nextId++,
                ClientAssignmentId = clientAssignmentId,
                SowType = type,
                SowStartDate = start,
                SowEndDate = end,
                HasPassedApplicationValidation = true,
            };
            _rows.Add(sow);
            return sow;
        }

        public Task<IReadOnlyList<Sow>> GetByAssignmentIdAsync(int clientAssignmentId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Sow>>([.. _rows.Where(s => s.ClientAssignmentId == clientAssignmentId)]);

        public Task<Sow?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.SingleOrDefault(s => s.Id == id));

        // ─── The three members feature 010's migration path added to this interface ───
        // Unused by THIS surface's tests: CompassSowService reaches assignment existence through
        // ICompassAssignmentRepository, and its reads are GetByAssignmentIdAsync above. They are
        // implemented faithfully rather than thrown, so a future test that does reach one gets a
        // working double instead of a surprise. See CompassSowMigrationServiceTests for the surface
        // that exercises them.

        /// <summary>Always true — this double models SOWs, not the assignment table.</summary>
        public Task<bool> AssignmentExistsAsync(int clientAssignmentId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<CompassSowDto>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CompassSowDto>>([.. _rows.Select(ToMigrationDto)]);

        public Task<CompassSowDto?> GetAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.SingleOrDefault(s => s.Id == id) is { } sow ? ToMigrationDto(sow) : null);

        private static CompassSowDto ToMigrationDto(Sow sow) =>
            new(
                sow.Id,
                sow.ClientAssignmentId,
                sow.SowType,
                sow.RateIncrease,
                sow.HasPassedApplicationValidation,
                sow.SowStartDate,
                sow.SowEndDate,
                sow.Note);

        public Task AddAsync(Sow sow, CancellationToken cancellationToken)
        {
            _pending.Add(sow);
            return Task.CompletedTask;
        }

        /// <summary>Issue #593 — removes the row immediately, matching this double's model for
        /// everything but Create (which alone needs the identity-column simulation).</summary>
        public List<Sow> Removed { get; } = [];

        public Task RemoveAsync(Sow sow, CancellationToken cancellationToken)
        {
            _rows.Remove(sow);
            Removed.Add(sow);
            return Task.CompletedTask;
        }

        /// <summary>Mirrors the real repository's exclusion-scope query (`ex_sow_no_overlap_per_assignment`).</summary>
        public Task<IReadOnlyList<Sow>> GetOverlappingAsync(
            int clientAssignmentId,
            DateOnly candidateStartDate,
            DateOnly candidateEndDate,
            int? excludingSowId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Sow>>(
                [.. _rows
                    .Where(s => s.ClientAssignmentId == clientAssignmentId)
                    .Where(s => excludingSowId == null || s.Id != excludingSowId)
                    .Where(s => s.SowStartDate <= candidateEndDate && candidateStartDate <= s.SowEndDate)]);

        /// <summary>Simulates the identity column: the real id is assigned only when "saved".</summary>
        public int CommitPending()
        {
            var count = _pending.Count;
            foreach (var sow in _pending)
            {
                sow.Id = _nextId++;
                _rows.Add(sow);
            }
            _pending.Clear();
            return count;
        }
    }

    private sealed class FakeAssignmentExistenceRepository : ICompassAssignmentRepository
    {
        private const int ClientId = 10;

        private readonly Dictionary<int, bool> _existingIds = [];

        /// <summary>
        /// Registers an assignment id as existing, optionally under an INTERNAL ("beach") client.
        /// </summary>
        /// <remarks>
        /// The <see cref="ClientAssignment.Client"/> navigation is populated, not left null
        /// (issue #518). The create guard reads the client's internal-EDJE flag through it, and the
        /// real repository's <c>GetByIdAsync</c> eager-loads <c>Client</c> — a double that returned a
        /// bare <c>ClientAssignment</c> could not express the case the guard turns on at all, which is
        /// exactly the shape of double that lets a guard look tested while never being reached.
        /// </remarks>
        public void SeedAssignment(int id, bool clientIsInternal = false) =>
            _existingIds[id] = clientIsInternal;

        public Task<ClientAssignment?> GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            if (!_existingIds.TryGetValue(id, out var clientIsInternal))
            {
                return Task.FromResult<ClientAssignment?>(null);
            }

            return Task.FromResult<ClientAssignment?>(new ClientAssignment
            {
                Id = id,
                ClientId = ClientId,
                Client = new Client
                {
                    Id = ClientId,
                    ClientName = clientIsInternal ? "EDJE (beach)" : "Buckeye Mutual",
                    IsInternal = clientIsInternal,
                },
            });
        }

        public Task<IReadOnlyList<ClientAssignment>> GetAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddAsync(ClientAssignment assignment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(ClientAssignment assignment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClientAssignment>> GetOpenAssignmentsByEmployeeAsync(
            int employeeId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Employee?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Client?> GetClientAsync(int clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClientPickerRowDto>> GetClientPickerRowsAsync(
            DateOnly today, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<EdjerPickerRowDto>> GetActiveEdjerPickerRowsAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        /// <summary>Unused by the SOW service — it carries no invoice-frequency override.</summary>
        public Task<bool> ActiveInvoiceFrequencyTypeExistsAsync(
            int invoiceFrequencyTypeId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork(FakeSowRepository repository) : ICompassUnitOfWork
    {
        /// <summary>When set, the next save throws this instead of committing (research R-4).</summary>
        public DbUpdateException? ThrowOnSave { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (ThrowOnSave is { } exception)
            {
                throw exception;
            }

            return Task.FromResult(repository.CommitPending());
        }

        /// <summary>
        /// Runs the operation with no transaction: these are unit tests over an in-memory double, where
        /// there is nothing to commit. The transactional guarantee itself is asserted against real
        /// Postgres by <c>CompassAuditAtomicityTests</c>, because only a real provider has transactions.
        /// </summary>
        public Task<T> ExecuteAtomicallyAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            Func<T, bool> commitWhen,
            CancellationToken cancellationToken) => operation(cancellationToken);
    }

    /// <summary>Fabricates a <see cref="DbUpdateException"/> carrying a specific SQLSTATE, mirroring
    /// <c>CompassWriteFailureTests.FailureWithSqlState</c>.</summary>
    private static DbUpdateException FailureWithSqlState(string sqlState) =>
        new(
            "An error occurred while saving the entity changes.",
            new PostgresException(
                messageText: "simulated violation",
                severity: "ERROR",
                invariantSeverity: "ERROR",
                sqlState: sqlState));

    private sealed class RecordingAuditService : IAuditService
    {
        public List<AuditEntry> Entries { get; } = [];

        /// <summary>When set, the next log throws this instead of recording (research R-4).</summary>
        public DbUpdateException? ThrowOnLog { get; set; }

        public Task LogAsync(AuditEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Reason))
            {
                throw new ArgumentException("Reason is required for audit log entries.", nameof(entry));
            }

            if (ThrowOnLog is { } exception)
            {
                throw exception;
            }

            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditLogResponse>> GetByEntityAsync(string entityType, string entityId) =>
            throw new NotSupportedException();

        public Task<PaginatedAuditLogResponse> BrowseAsync(
            string? entityType, string? actor, string? employeeId, DateTime? fromDate, DateTime? toDate, int page, int pageSize) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetDistinctEntityTypesAsync() => throw new NotSupportedException();
    }

    private sealed class FakeCurrentUserContext(Guid edjeId, params string[] privileges) : ICurrentUserContext
    {
        public Guid EdjeId { get; } = edjeId;
        public string Email { get; } = "ops@example.test";
        public string TpsEmployeeId { get; } = "1";
        public IReadOnlyList<string> Privileges { get; } = privileges;
        public bool HasPrivilege(string privilege) => Privileges.Contains(privilege);
    }

    private static (
        CompassSowService Service,
        FakeSowRepository Sows,
        FakeAssignmentExistenceRepository Assignments,
        RecordingAuditService Audit,
        FakeUnitOfWork UnitOfWork)
        Build()
    {
        var sows = new FakeSowRepository();
        var assignments = new FakeAssignmentExistenceRepository();
        var unitOfWork = new FakeUnitOfWork(sows);
        var audit = new RecordingAuditService();
        var currentUser = new FakeCurrentUserContext(Guid.NewGuid(), "Compass Ops");

        var service = new CompassSowService(
            sows, assignments, unitOfWork, audit, currentUser, new RecordingCoachNotifier());

        return (service, sows, assignments, audit, unitOfWork);
    }

    /// <summary>
    /// The same service, with the coach notifier exposed so the WIRING can be asserted.
    /// </summary>
    /// <remarks>
    /// A separate helper rather than a sixth tuple member on <c>Build</c>: only these tests care, and
    /// widening the shared shape would touch every call site in this file for no benefit to them.
    /// </remarks>
    private static (
        CompassSowService Service,
        FakeAssignmentExistenceRepository Assignments,
        RecordingCoachNotifier Notifier)
        BuildWithNotifier()
    {
        var sows = new FakeSowRepository();
        var assignments = new FakeAssignmentExistenceRepository();
        var notifier = new RecordingCoachNotifier();
        var service = new CompassSowService(
            sows,
            assignments,
            new FakeUnitOfWork(sows),
            new RecordingAuditService(),
            new FakeCurrentUserContext(Guid.NewGuid(), "Compass Ops"),
            notifier);

        return (service, assignments, notifier);
    }

    // ------------------------------------------ US4/#68: WHICH period type asks for a notice

    [Fact]
    public async Task CreateAsync_AnExtension_AsksForTheCoachNotice()
    {
        // Arrange — FR-026, AC-32.
        var (service, assignments, notifier) = BuildWithNotifier();
        assignments.SeedAssignment(AssignmentId);
        var request = new CreateSowRequest(
            SowType.SowExtension, RateIncrease: false, Start, End, null);

        // Act
        var result = await service.CreateAsync(
            AssignmentId, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        notifier.SowExtensionsAdded.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task CreateAsync_AnInitialContract_AsksForNoNotice()
    {
        // Arrange — FR-028, AC-31. The first contract period is the engagement STARTING, not changing.
        // This is the distinction the whole trigger turns on, and it was unasserted at this level.
        var (service, assignments, notifier) = BuildWithNotifier();
        assignments.SeedAssignment(AssignmentId);
        var request = new CreateSowRequest(
            SowType.InitialContract, RateIncrease: false, Start, End, null);

        // Act
        var result = await service.CreateAsync(
            AssignmentId, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        notifier.SowExtensionsAdded.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_ChangingAPeriodToAnExtension_AsksForNoNotice()
    {
        // Arrange — FR-027. Only an INITIAL add notifies; an edit never does, whatever it changes the
        // type to. Editing is not the engagement changing in the way a coach needs to hear about.
        var (service, assignments, notifier) = BuildWithNotifier();
        assignments.SeedAssignment(AssignmentId);
        var created = await service.CreateAsync(
            AssignmentId,
            new CreateSowRequest(SowType.InitialContract, RateIncrease: false, Start, End, null),
            TestContext.Current.CancellationToken);
        created.Status.ShouldBe(AdminMutationStatus.Success);

        // Act
        await service.UpdateAsync(
            AssignmentId,
            created.Value!.Id,
            new UpdateSowRequest(SowType.SowExtension, RateIncrease: true, Start, End, null),
            TestContext.Current.CancellationToken);

        // Assert
        notifier.SowExtensionsAdded.ShouldBeEmpty();
    }

    // ----------------------------- Issue #518: an internal client's assignment has no SOWs

    [Fact]
    public async Task CreateAsync_UnderAnInternalClient_ReturnsInvalid()
    {
        // Arrange — issue #518. An internal ("beach") client is EDJE working on EDJE: there is no
        // counterparty to sign a statement of work with, and nothing to invoice.
        var (service, _, assignments, _, _) = Build();
        assignments.SeedAssignment(AssignmentId, clientIsInternal: true);
        var request = new CreateSowRequest(
            SowType.InitialContract, RateIncrease: false, Start, End, null);

        // Act
        var result = await service.CreateAsync(
            AssignmentId, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
        result.Error.ShouldBe("An internal client's assignment has no statements of work.");
    }

    /// <summary>
    /// The control for the test above: create still succeeds under an ordinary client.
    /// </summary>
    /// <remarks>
    /// Without it the refusal test passes just as well against a guard that refuses EVERY create — so
    /// it is what proves the rule is keyed on the client's internal flag rather than simply breaking
    /// the path. The same reason this suite bans a loose denial assertion.
    /// </remarks>
    [Fact]
    public async Task CreateAsync_UnderAnExternalClient_StillSucceeds()
    {
        // Arrange
        var (service, _, assignments, _, _) = Build();
        assignments.SeedAssignment(AssignmentId, clientIsInternal: false);
        var request = new CreateSowRequest(
            SowType.InitialContract, RateIncrease: false, Start, End, null);

        // Act
        var result = await service.CreateAsync(
            AssignmentId, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
    }

    [Fact]
    public async Task UpdateAsync_UnderAnInternalClient_IsStillAllowed()
    {
        // Arrange — issue #518 deliberately guards CREATE only. An existing row (in practice a
        // LegacyMigrated one the TPS migration loaded) must stay correctable: the guard's job is to
        // stop NEW contract periods being opened against work that is never invoiced, not to strand
        // history that already exists.
        var (service, sows, assignments, _, _) = Build();
        assignments.SeedAssignment(AssignmentId, clientIsInternal: true);
        var existing = sows.Seed(AssignmentId, Start, End);
        var request = new UpdateSowRequest(
            SowType.InitialContract, RateIncrease: false, Start, End, "Corrected");

        // Act
        var result = await service.UpdateAsync(
            AssignmentId, existing.Id, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        existing.Note.ShouldBe("Corrected");
    }

    // ---------------------------------------------------------------- FR-016

    [Fact]
    public async Task CreateAsync_WithLegacyMigrated_ReturnsInvalid()
    {
        // Arrange
        var (service, _, assignments, _, _) = Build();
        assignments.SeedAssignment(AssignmentId);
        var request = new CreateSowRequest(SowType.LegacyMigrated, RateIncrease: false, Start, End, null);

        // Act
        var result = await service.CreateAsync(AssignmentId, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
    }

    [Fact]
    public async Task UpdateAsync_WithLegacyMigrated_ReturnsInvalid()
    {
        // Arrange
        var (service, sows, _, _, _) = Build();
        var existing = sows.Seed(AssignmentId, Start, End);
        var request = new UpdateSowRequest(SowType.LegacyMigrated, RateIncrease: false, Start, End, null);

        // Act
        var result = await service.UpdateAsync(AssignmentId, existing.Id, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheExistingSowIsLegacyMigrated_ReturnsInvalid()
    {
        // Arrange — the request asks for an ordinary type (SowExtension), i.e. it tries to relabel a
        // legacy row as a normal one.
        //
        // **What this test asserts changed meaning in #67, while its expectation did not.** Under #66 a
        // legacy row was read-only outright; AC-41 requires the opposite, so it is now editable and the
        // edit is fully validated. What remains refused is changing its TYPE: "LegacyMigrated" records
        // that the row predates this application's rules, and letting an ordinary edit relabel it would
        // erase the reason it was ever exempt. Correct the dates, keep the history.
        var (service, sows, _, _, _) = Build();
        var existing = sows.Seed(AssignmentId, Start, End, SowType.LegacyMigrated);
        existing.HasPassedApplicationValidation = false;
        var request = new UpdateSowRequest(SowType.SowExtension, RateIncrease: false, Start, End, null);

        // Act
        var result = await service.UpdateAsync(AssignmentId, existing.Id, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
        existing.SowType.ShouldBe(SowType.LegacyMigrated, "the existing row must not be mutated by a rejected update");
        existing.HasPassedApplicationValidation.ShouldBeFalse("a refused edit does not validate the row");
    }

    /// <summary>
    /// AC-41/FR-046 — a legacy row KEEPING its type is editable, fully validated, and marked validated.
    /// </summary>
    /// <remarks>
    /// The positive half of the test above, and the behaviour #67 exists to add. Without it
    /// <see cref="Sow.HasPassedApplicationValidation"/> is dead for the only rows that ever carry it
    /// <c>false</c>.
    /// </remarks>
    [Fact]
    public async Task UpdateAsync_WhenALegacySowKeepsItsType_IsAcceptedAndMarkedValidated()
    {
        // Arrange
        var (service, sows, _, _, _) = Build();
        var existing = sows.Seed(AssignmentId, Start, End, SowType.LegacyMigrated);
        existing.HasPassedApplicationValidation = false;
        var request = new UpdateSowRequest(SowType.LegacyMigrated, RateIncrease: false, Start, End, null);

        // Act
        var result = await service.UpdateAsync(
            AssignmentId, existing.Id, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        existing.HasPassedApplicationValidation.ShouldBeTrue("the first valid edit is AC-41's exit");
        existing.SowType.ShouldBe(SowType.LegacyMigrated, "validation records a fact, it does not re-type");
    }

    /// <summary>
    /// AC-41 — the edit is still fully validated: an invalid legacy edit is refused (FR-046).
    /// </summary>
    /// <remarks>
    /// The guard against reading "legacy rows are editable now" as "legacy rows skip validation". The
    /// exemption is an EXIT from legacy state, never a licence to keep writing invalid periods.
    /// </remarks>
    [Fact]
    public async Task UpdateAsync_WhenALegacySowIsStillInvalid_IsRefusedAndNotMarkedValidated()
    {
        // Arrange — end before start (FR-020).
        var (service, sows, _, _, _) = Build();
        var existing = sows.Seed(AssignmentId, Start, End, SowType.LegacyMigrated);
        existing.HasPassedApplicationValidation = false;
        var request = new UpdateSowRequest(SowType.LegacyMigrated, RateIncrease: false, End, Start, null);

        // Act
        var result = await service.UpdateAsync(
            AssignmentId, existing.Id, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
        existing.HasPassedApplicationValidation.ShouldBeFalse();
    }

    // ---------------------------------------------------------------- FR-004-equivalent (assignment/SOW existence)

    [Fact]
    public async Task CreateAsync_WhenTheAssignmentDoesNotExist_ReturnsNotFound()
    {
        // Arrange — no assignment seeded at all.
        var (service, _, _, _, _) = Build();
        var request = new CreateSowRequest(SowType.InitialContract, RateIncrease: false, Start, End, null);

        // Act
        var result = await service.CreateAsync(999_999, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.NotFound);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheSowDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var (service, _, _, _, _) = Build();
        var request = new UpdateSowRequest(SowType.InitialContract, RateIncrease: false, Start, End, null);

        // Act
        var result = await service.UpdateAsync(AssignmentId, 999_999, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.NotFound);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheNewDatesOverlapAnotherPeriod_IsRefusedNamingThatPeriod()
    {
        // Arrange — two periods on one assignment, and the second is edited to run back over the
        // first. The overlap check excludes the SOW being edited (otherwise every edit would collide
        // with itself), so what it finds here is a genuine clash with its neighbour. FR-023 wants the
        // clashing period NAMED, and issue #234 governs how those two dates are rendered.
        var (service, sows, _, _, _) = Build();
        sows.Seed(AssignmentId, new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30));
        var second = sows.Seed(AssignmentId, new DateOnly(2024, 7, 1), new DateOnly(2024, 12, 31));

        // Act
        var result = await service.UpdateAsync(
            AssignmentId,
            second.Id,
            new UpdateSowRequest(
                SowType.InitialContract,
                RateIncrease: false,
                new DateOnly(2024, 5, 1),
                new DateOnly(2024, 12, 31),
                null),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
        result.Error.ShouldBe(
            "Dates overlap an existing SOW for this assignment (01/01/2024 – 06/30/2024).");
    }

    [Fact]
    public async Task UpdateAsync_MovingTheStartDate_RecordsTheOldAndNewValues()
    {
        // Arrange — a period edited to start a month earlier, with nothing else touched. The audit
        // entry is the record of WHAT changed, so a date move must appear in it as one FieldChange
        // carrying both values; `ToString("O")` here is round-trip serialisation, not display, which
        // is the distinction CompassDisplayDateTests deliberately preserves.
        var (service, sows, _, audit, _) = Build();
        var existing = sows.Seed(AssignmentId, new DateOnly(2024, 2, 1), new DateOnly(2024, 12, 31));

        // Act
        var result = await service.UpdateAsync(
            AssignmentId,
            existing.Id,
            new UpdateSowRequest(
                SowType.InitialContract,
                RateIncrease: false,
                new DateOnly(2024, 1, 1),
                new DateOnly(2024, 12, 31),
                null),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        var change = audit.Entries.ShouldHaveSingleItem().Changes.ShouldHaveSingleItem();
        change.Field.ShouldBe("SowStartDate");
        change.Before.ShouldBe("2024-02-01");
        change.After.ShouldBe("2024-01-01");
    }

    [Fact]
    public async Task UpdateAsync_WhenTheSowBelongsToADifferentAssignment_ReturnsNotFound()
    {
        // Arrange — an IDOR-style guard: the route's assignmentId and the SOW's owning assignment must agree.
        var (service, sows, _, _, _) = Build();
        var existing = sows.Seed(clientAssignmentId: 2, Start, End);
        var request = new UpdateSowRequest(SowType.InitialContract, RateIncrease: false, Start, End, null);

        // Act
        var result = await service.UpdateAsync(AssignmentId, existing.Id, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.NotFound);
    }

    // ---------------------------------------------------------------- FR-017

    [Fact]
    public async Task CreateAsync_WithRateIncreaseOnInitialContract_ReturnsInvalid()
    {
        // Arrange
        var (service, _, assignments, _, _) = Build();
        assignments.SeedAssignment(AssignmentId);
        var request = new CreateSowRequest(SowType.InitialContract, RateIncrease: true, Start, End, null);

        // Act
        var result = await service.CreateAsync(AssignmentId, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
    }

    // ---------------------------------------------------------------- FR-020

    [Fact]
    public async Task CreateAsync_WithEndDateBeforeStartDate_ReturnsInvalid()
    {
        // Arrange
        var (service, _, assignments, _, _) = Build();
        assignments.SeedAssignment(AssignmentId);
        var request = new CreateSowRequest(SowType.InitialContract, RateIncrease: false, End, Start, null);

        // Act
        var result = await service.CreateAsync(AssignmentId, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
    }

    // ---------------------------------------------------------------- FR-019

    [Fact]
    public async Task CreateAsync_WithAnOverlappingPeriod_ReturnsConflict_NamingThePeriod()
    {
        // Arrange
        var (service, sows, assignments, _, _) = Build();
        assignments.SeedAssignment(AssignmentId);
        sows.Seed(AssignmentId, Start, End);
        var overlapping = new CreateSowRequest(
            SowType.SowExtension, RateIncrease: false, Start.AddMonths(6), End.AddMonths(6), null);

        // Act
        var result = await service.CreateAsync(AssignmentId, overlapping, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
        result.Error.ShouldNotBeNullOrWhiteSpace();

        // mm/dd/yyyy, not the wire format (issue #234, #306). A user reads this message verbatim in
        // the SOW form's whole-form error, so it is a DISPLAYED date like any other. Asserted through
        // CompassDisplayDate rather than as a literal so the two cannot drift apart.
        result.Error.ShouldContain(CompassDisplayDate.Format(Start));
        result.Error.ShouldNotContain(
            Start.ToString("yyyy-MM-dd"),
            Case.Sensitive,
            "the ISO wire format is exactly what #234 exists to keep off a screen"
        );
    }

    [Fact]
    public async Task CreateAsync_WithAGapBeforeAnExistingPeriod_Succeeds()
    {
        // Arrange — FR-021: gaps are allowed.
        var (service, sows, assignments, _, _) = Build();
        assignments.SeedAssignment(AssignmentId);
        sows.Seed(AssignmentId, Start, End);
        var gapped = new CreateSowRequest(
            SowType.SowExtension, RateIncrease: false, End.AddDays(1).AddMonths(1), End.AddMonths(12), null);

        // Act
        var result = await service.CreateAsync(AssignmentId, gapped, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
    }

    // ---------------------------------------------------------------- happy path, FR-024, audit

    [Fact]
    public async Task CreateAsync_Succeeds_SetsHasPassedApplicationValidationTrue_AndAudits()
    {
        // Arrange
        var (service, sows, assignments, audit, _) = Build();
        assignments.SeedAssignment(AssignmentId);
        var request = new CreateSowRequest(SowType.InitialContract, RateIncrease: false, Start, End, "A note");

        // Act
        var result = await service.CreateAsync(AssignmentId, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        result.Value.ShouldNotBeNull();

        var stored = await sows.GetByIdAsync(result.Value!.Id, TestContext.Current.CancellationToken);
        stored!.HasPassedApplicationValidation.ShouldBeTrue();

        audit.Entries.ShouldHaveSingleItem();
        var entry = audit.Entries[0];
        entry.EntityType.ShouldBe("CompassSow");
        entry.EntityId.ShouldBe(result.Value.Id.ToString());
        entry.Action.ShouldBe("create");
        entry.Reason.ShouldNotBeNullOrWhiteSpace();
        entry.EffectiveRoles.ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_Succeeds_RecordsFieldChanges_AndAudits()
    {
        // Arrange
        var (service, sows, _, audit, _) = Build();
        var existing = sows.Seed(AssignmentId, Start, End);
        var newNote = "Renewed";
        var request = new UpdateSowRequest(existing.SowType, RateIncrease: false, Start, End, newNote);

        // Act
        var result = await service.UpdateAsync(AssignmentId, existing.Id, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        audit.Entries.ShouldHaveSingleItem();
        var entry = audit.Entries[0];
        entry.Action.ShouldBe("update");
        var change = entry.Changes.Single(c => c.Field == "Note");
        change.Before.ShouldBeNull();
        change.After.ShouldBe(newNote);
    }

    // ---------------------------------------------------------------- research R-4 translation

    [Fact]
    public async Task CreateAsync_WhenSaveLosesARaceOnATranslatableSqlState_ReturnsConflict()
    {
        // Arrange — the exclusion constraint fires on the save despite the service's own overlap
        // pre-check (data-model §5, research R-4).
        var (service, _, assignments, audit, unitOfWork) = Build();
        assignments.SeedAssignment(AssignmentId);
        unitOfWork.ThrowOnSave = FailureWithSqlState(CompassWriteFailure.ExclusionViolationSqlState);
        var request = new CreateSowRequest(SowType.InitialContract, RateIncrease: false, Start, End, null);

        // Act
        var result = await service.CreateAsync(AssignmentId, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
        result.Error.ShouldNotBeNullOrWhiteSpace();
        audit.Entries.ShouldBeEmpty("a failed save must never reach LogAsync");
    }

    [Fact]
    public async Task UpdateAsync_WhenLogAsyncLosesARaceOnATranslatableSqlState_ReturnsConflict()
    {
        // Arrange — for Update, IAuditService.LogAsync IS the commit, so this is where a lost race
        // on the CHECK constraint actually surfaces.
        var (service, sows, _, audit, _) = Build();
        var existing = sows.Seed(AssignmentId, Start, End);
        audit.ThrowOnLog = FailureWithSqlState(CompassWriteFailure.CheckViolationSqlState);
        var request = new UpdateSowRequest(existing.SowType, RateIncrease: false, Start, End.AddDays(30), null);

        // Act
        var result = await service.UpdateAsync(AssignmentId, existing.Id, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    // ---------------------------------------------------------------- reads

    [Fact]
    public async Task GetByAssignmentIdAsync_ReturnsEveryPeriodForThatAssignment()
    {
        // Arrange
        var (service, sows, _, _, _) = Build();
        sows.Seed(AssignmentId, Start, End);
        sows.Seed(clientAssignmentId: 2, Start, End);

        // Act
        var rows = await service.GetByAssignmentIdAsync(AssignmentId, TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldHaveSingleItem();
    }

    // ---------------------------------------------------------------- issue #593: DeleteAsync

    [Fact]
    public async Task DeleteAsync_WhenTheSowDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var (service, _, _, _, _) = Build();

        // Act
        var status = await service.DeleteAsync(AssignmentId, 999_999, TestContext.Current.CancellationToken);

        // Assert
        status.ShouldBe(AdminMutationStatus.NotFound);
    }

    [Fact]
    public async Task DeleteAsync_WhenTheSowBelongsToADifferentAssignment_ReturnsNotFound()
    {
        // Arrange — the same IDOR-style guard UpdateAsync applies.
        var (service, sows, _, _, _) = Build();
        var existing = sows.Seed(clientAssignmentId: 2, Start, End);

        // Act
        var status = await service.DeleteAsync(AssignmentId, existing.Id, TestContext.Current.CancellationToken);

        // Assert
        status.ShouldBe(AdminMutationStatus.NotFound);
    }

    [Fact]
    public async Task DeleteAsync_Succeeds_RemovesTheSow_AndAuditsWithANonBlankReason()
    {
        // Arrange
        var (service, sows, _, audit, _) = Build();
        var existing = sows.Seed(AssignmentId, Start, End);

        // Act
        var status = await service.DeleteAsync(AssignmentId, existing.Id, TestContext.Current.CancellationToken);

        // Assert
        status.ShouldBe(AdminMutationStatus.Success);
        (await sows.GetByIdAsync(existing.Id, TestContext.Current.CancellationToken)).ShouldBeNull();
        sows.Removed.ShouldBe([existing]);

        var entry = audit.Entries.ShouldHaveSingleItem();
        entry.EntityType.ShouldBe("CompassSow");
        entry.EntityId.ShouldBe(existing.Id.ToString());
        entry.Action.ShouldBe("delete");
        entry.Reason.ShouldNotBeNullOrWhiteSpace();
        entry.EffectiveRoles.ShouldNotBeNull();
    }
}
