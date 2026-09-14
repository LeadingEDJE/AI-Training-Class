using System.Linq.Expressions;
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
/// US1/#62 — the assignment service's rejection paths (FR-003, FR-004, FR-008) and the FR-005/research
/// R-1 delegation to <see cref="IClientStatusDerivation"/>.
/// </summary>
/// <remarks>
/// Hand-written in-memory doubles rather than a mocking framework, matching
/// <c>CompassLookupServiceTests</c> and the project's test conventions.
/// </remarks>
public class CompassAssignmentServiceTests
{
    private const int EmployeeId = 1;
    private const int ClientId = 10;
    private static readonly DateOnly Today = new(2026, 6, 1);

    private sealed class FakeAssignmentRepository : ICompassAssignmentRepository
    {
        private readonly List<ClientAssignment> _rows = [];
        private readonly List<ClientAssignment> _pending = [];
        private readonly List<Employee> _employees = [];
        private readonly List<Client> _clients = [];
        private int _nextId = 1;

        public void SeedEmployee(int id, bool isActive = true, string lastName = "Lovelace") =>
            _employees.Add(new Employee { Id = id, FirstName = "Ada", LastName = lastName, IsActive = isActive });

        public void SeedClient(int id, string clientName = "Acme") =>
            _clients.Add(new Client { Id = id, ClientName = clientName });

        public ClientAssignment Seed(int employeeId, int clientId, DateOnly startDate, DateOnly? endDate = null, string? note = null)
        {
            var assignment = new ClientAssignment
            {
                Id = _nextId++,
                EmployeeId = employeeId,
                ClientId = clientId,
                StartDate = startDate,
                EndDate = endDate,
                Note = note,
                Employee = _employees.FirstOrDefault(e => e.Id == employeeId),
            };
            _rows.Add(assignment);
            return assignment;
        }

        public Task<IReadOnlyList<ClientAssignment>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClientAssignment>>([.. _rows]);

        public Task<ClientAssignment?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.SingleOrDefault(a => a.Id == id));

        public Task AddAsync(ClientAssignment assignment, CancellationToken cancellationToken)
        {
            _pending.Add(assignment);
            return Task.CompletedTask;
        }

        /// <summary>Issue #593 — removes the row IMMEDIATELY, matching this double's no-staging model
        /// for everything except Create (which alone needs the identity-column simulation).</summary>
        public Task RemoveAsync(ClientAssignment assignment, CancellationToken cancellationToken)
        {
            _rows.Remove(assignment);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ClientAssignment>> GetOpenAssignmentsByEmployeeAsync(
            int employeeId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClientAssignment>>(
                [.. _rows.Where(a => a.EmployeeId == employeeId && a.EndDate is null)]);

        public Task<Employee?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken) =>
            Task.FromResult(_employees.FirstOrDefault(e => e.Id == employeeId));

        public Task<Client?> GetClientAsync(int clientId, CancellationToken cancellationToken) =>
            Task.FromResult(_clients.FirstOrDefault(c => c.Id == clientId));

        /// <summary>
        /// The cadences this double treats as SELECTABLE (US6, #64).
        /// </summary>
        /// <remarks>
        /// Ids rather than rows with an active flag: the flag's meaning is the real repository's SQL
        /// predicate, and a double re-deciding it here would be a second copy of the rule. Empty by
        /// default, so a test that sets an override without registering it as active sees the refusal —
        /// which is the direction that matters.
        /// </remarks>
        public HashSet<int> SelectableInvoiceFrequencyTypeIds { get; } = [];

        public Task<bool> ActiveInvoiceFrequencyTypeExistsAsync(
            int invoiceFrequencyTypeId, CancellationToken cancellationToken) =>
            Task.FromResult(SelectableInvoiceFrequencyTypeIds.Contains(invoiceFrequencyTypeId));

        public List<ClientPickerRowDto> ClientPickerRows { get; } = [];

        public Task<IReadOnlyList<ClientPickerRowDto>> GetClientPickerRowsAsync(
            DateOnly today, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClientPickerRowDto>>([.. ClientPickerRows]);

        public List<EdjerPickerRowDto> EdjerPickerRows { get; } = [];

        public Task<IReadOnlyList<EdjerPickerRowDto>> GetActiveEdjerPickerRowsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EdjerPickerRowDto>>([.. EdjerPickerRows]);

        /// <summary>Simulates the identity column: the real id is assigned only when "saved".</summary>
        public int CommitPending()
        {
            var count = _pending.Count;
            foreach (var assignment in _pending)
            {
                assignment.Id = _nextId++;
                assignment.Employee ??= _employees.FirstOrDefault(e => e.Id == assignment.EmployeeId);
                _rows.Add(assignment);
            }
            _pending.Clear();
            return count;
        }
    }

    /// <summary>
    /// Issue #593 — the SOW-side of an assignment delete's cascade. Records which rows were removed
    /// so a test can assert the cascade reached them; every other member throws, matching this file's
    /// convention for a dependency <see cref="CompassAssignmentService"/> uses for exactly one thing.
    /// </summary>
    private sealed class RecordingSowRepository : ICompassSowRepository
    {
        public List<Sow> Removed { get; } = [];

        public Task RemoveAsync(Sow sow, CancellationToken cancellationToken)
        {
            Removed.Add(sow);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Sow>> GetByAssignmentIdAsync(int clientAssignmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<Sow?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task AddAsync(Sow sow, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<Sow>> GetOverlappingAsync(
            int clientAssignmentId, DateOnly candidateStartDate, DateOnly candidateEndDate,
            int? excludingSowId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> AssignmentExistsAsync(int clientAssignmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<CompassSowDto>> GetAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<CompassSowDto?> GetAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork(FakeAssignmentRepository repository) : ICompassUnitOfWork
    {
        /// <summary>
        /// Runs the operation with no transaction: these are unit tests over an in-memory double, where
        /// there is nothing to commit. The transactional guarantee itself is asserted against real
        /// Postgres by <c>CompassAuditAtomicityTests</c>, because only a real provider has transactions.
        /// </summary>
        public async Task<T> ExecuteAtomicallyAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            Func<T, bool> commitWhen,
            CancellationToken cancellationToken)
        {
            AtomicScopeActive = true;
            try
            {
                var result = await operation(cancellationToken);

                // Asked exactly as the real unit of work asks it. There is no transaction to commit
                // or roll back here, but the PREDICATE is part of what a caller passes in, and one
                // that is never invoked is never checked against the result type it is written for.
                CommitDecisions.Add(commitWhen(result));
                return result;
            }
            finally
            {
                AtomicScopeActive = false;
            }
        }

        /// <summary>
        /// Whether an atomic scope is open right now. The real unit of work opens its transaction on
        /// the first save INSIDE such a scope, so anything that saves while this is true is enrolled in
        /// the caller's transaction.
        /// </summary>
        public bool AtomicScopeActive { get; private set; }

        /// <summary>What <c>commitWhen</c> answered for each scope, in order.</summary>
        public List<bool> CommitDecisions { get; } = [];

        public int SaveCount { get; private set; }

        /// <summary>When set, the next save throws this instead of committing (research R-4).</summary>
        public DbUpdateException? ThrowOnSave { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (ThrowOnSave is { } exception)
            {
                throw exception;
            }

            SaveCount++;
            return Task.FromResult(repository.CommitPending());
        }
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

    private sealed class FakeBusinessDate(DateOnly today) : ICompassBusinessDate
    {
        public DateOnly Today() => today;
    }

    /// <summary>
    /// Proves delegation rather than local computation (FR-005, research R-1): returns a value the
    /// REAL <c>ClientStatusDerivation</c> would never produce for these rows, so a service that computed
    /// currency itself instead of using this double would fail the assertion, not merely go unnoticed.
    /// </summary>
    private sealed class RecordingClientStatusDerivation : IClientStatusDerivation
    {
        public bool IsCurrentWasCalled { get; private set; }

        public Expression<Func<ClientAssignment, bool>> IsCurrent(DateOnly today)
        {
            IsCurrentWasCalled = true;
            return _ => false;
        }

        public Expression<Func<Client, bool>> IsActive(DateOnly today) => throw new NotSupportedException();
        public ClientStatus Of(Client client, DateOnly today) => throw new NotSupportedException();

        // Issue #274 split `From(bool)` into one naming method per level and added the predicate that
        // separates Inactive from Former. Throwing, per the convention above: the assignment service
        // names no status of its own.
        public Expression<Func<Client, bool>> HasEverBeenAssigned() =>
            throw new NotSupportedException();
        public ClientStatus StatusOfClient(bool holdsCurrentAssignment, bool hasEverBeenAssigned) =>
            throw new NotSupportedException();
        public AssignmentStatus StatusOfAssignment(bool isCurrent) =>
            throw new NotSupportedException();

        // Added when feature 007 merged in: it grew IClientStatusDerivation by three members, and a
        // double written against the older interface stops compiling. Throwing rather than returning
        // a plausible expression is this double's existing convention for anything the assignment
        // service does not call — a silent stub would let a future service quietly depend on one.
        public Expression<Func<ClientAssignment, bool>> IsFutureDated(DateOnly today) =>
            throw new NotSupportedException();
        public Expression<Func<Sow, bool>> IsExpiringWithin(DateOnly today, int days) =>
            throw new NotSupportedException();
        public Expression<Func<Sow, bool>> HasNoFollowOn() => throw new NotSupportedException();
        public Expression<Func<Sow, bool>> SowAssignmentIsOpenEndedAndActive(DateOnly today) =>
            throw new NotSupportedException();

        public Expression<Func<Sow, bool>> IsActiveSow(DateOnly today) =>
            throw new NotSupportedException();

        // A fourth arrived with feature 007's FR-032 correction (T009a): IsCurrent means "not ended",
        // so "active" needs HasStarted ANDed onto it. Throwing, per the convention above — the
        // assignment service does not derive currency itself, which is what this double proves.
        public Expression<Func<ClientAssignment, bool>> HasStarted(DateOnly today) =>
            throw new NotSupportedException();
    }

    private static (
        CompassAssignmentService Service,
        FakeAssignmentRepository Repository,
        RecordingAuditService Audit,
        RecordingClientStatusDerivation Derivation,
        FakeUnitOfWork UnitOfWork)
        Build(params string[] privileges)
    {
        var repository = new FakeAssignmentRepository();
        var unitOfWork = new FakeUnitOfWork(repository);
        var audit = new RecordingAuditService();
        var currentUser = new FakeCurrentUserContext(Guid.NewGuid(), privileges.Length == 0 ? ["Compass Ops"] : privileges);
        var derivation = new RecordingClientStatusDerivation();
        var businessDate = new FakeBusinessDate(Today);

        var service = new CompassAssignmentService(
            repository, new RecordingSowRepository(), unitOfWork, audit, currentUser, derivation,
            businessDate, new RecordingCoachNotifier());

        return (service, repository, audit, derivation, unitOfWork);
    }

    /// <summary>
    /// The same service, with the coach notifier exposed so the WIRING can be asserted.
    /// </summary>
    /// <remarks>
    /// A separate helper rather than a sixth tuple member on <c>Build</c>: only these tests care, and
    /// widening the shared shape would touch every call site in this file for no benefit to them.
    /// </remarks>
    private static (
        CompassAssignmentService Service,
        FakeAssignmentRepository Repository,
        RecordingCoachNotifier Notifier,
        FakeUnitOfWork UnitOfWork)
        BuildWithNotifier()
    {
        var repository = new FakeAssignmentRepository();
        var notifier = new RecordingCoachNotifier();
        var unitOfWork = new FakeUnitOfWork(repository);
        var service = new CompassAssignmentService(
            repository,
            new RecordingSowRepository(),
            unitOfWork,
            new RecordingAuditService(),
            new FakeCurrentUserContext(Guid.NewGuid(), ["Compass Ops"]),
            new RecordingClientStatusDerivation(),
            new FakeBusinessDate(Today),
            notifier);

        return (service, repository, notifier, unitOfWork);
    }

    // ------------------------------------------ US4/#68: WHEN the coach notice is asked for

    [Fact]
    public async Task UpdateAsync_EndDatingAnOpenAssignment_AsksForTheCoachNotice()
    {
        // Arrange — FR-025. The trigger is the transition from no end date to one.
        var (service, repository, notifier, _) = BuildWithNotifier();
        repository.SeedEmployee(EmployeeId);
        var existing = repository.Seed(EmployeeId, ClientId, Today);

        // Act
        await service.UpdateAsync(
            existing.Id,
            new UpdateAssignmentRequest(Today, Today.AddMonths(6), null),
            TestContext.Current.CancellationToken);

        // Assert
        notifier.AssignmentsEnded.ShouldBe([existing.Id]);
    }

    [Fact]
    public async Task UpdateAsync_EditingAnAssignmentThatIsAlreadyEndDated_AsksForNoNotice()
    {
        // Arrange — FR-027. The service-level half of "once, ever": a later edit is not a transition,
        // so the notifier is never even consulted. The notifier's own idempotency covers the case where
        // the end date is CLEARED and set again, which does transition a second time.
        var (service, repository, notifier, _) = BuildWithNotifier();
        repository.SeedEmployee(EmployeeId);
        var existing = repository.Seed(EmployeeId, ClientId, Today, Today.AddMonths(6));

        // Act
        await service.UpdateAsync(
            existing.Id,
            new UpdateAssignmentRequest(Today, Today.AddMonths(9), null),
            TestContext.Current.CancellationToken);

        // Assert
        notifier.AssignmentsEnded.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_AnEditThatDoesNotEndDate_AsksForNoNotice()
    {
        // Arrange — an ordinary note change on an open engagement notifies nobody.
        var (service, repository, notifier, _) = BuildWithNotifier();
        repository.SeedEmployee(EmployeeId);
        var existing = repository.Seed(EmployeeId, ClientId, Today);

        // Act
        await service.UpdateAsync(
            existing.Id,
            new UpdateAssignmentRequest(Today, null, "a note"),
            TestContext.Current.CancellationToken);

        // Assert
        notifier.AssignmentsEnded.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_EndDatingAnOpenAssignment_AsksForTheNoticeOnlyOnceTheAtomicScopeHasClosed()
    {
        // Arrange -- contract §6: the notice is dispatched AFTER the write commits, and asking for it
        // while the caller's atomic scope is still open is not merely early, it is unsafe. The real
        // unit of work opens its transaction on the first save inside a scope, and the notifier's
        // idempotency claim IS that first save here (the audit log saves through the context directly).
        // A lost claim race then aborts the scope's transaction, the notifier swallows the duplicate-key
        // exception, and the reload that follows this call runs in an aborted transaction -- 25P02, an
        // HTTP 500 on a write that had already committed.
        var (service, repository, notifier, unitOfWork) = BuildWithNotifier();
        repository.SeedEmployee(EmployeeId);
        var existing = repository.Seed(EmployeeId, ClientId, Today);
        bool? scopeWasOpenWhenAsked = null;
        notifier.OnNotify = () => scopeWasOpenWhenAsked = unitOfWork.AtomicScopeActive;

        // Act
        await service.UpdateAsync(
            existing.Id,
            new UpdateAssignmentRequest(Today, Today.AddMonths(6), null),
            TestContext.Current.CancellationToken);

        // Assert
        notifier.AssignmentsEnded.ShouldBe([existing.Id]);
        scopeWasOpenWhenAsked.ShouldBe(false);
    }

    // ---------------------------------------------------------------- FR-003

    [Fact]
    public async Task CreateAsync_WithAnInactiveEdjer_ReturnsInvalid()
    {
        // Arrange
        var (service, repository, _, _, _) = Build();
        repository.SeedEmployee(EmployeeId, isActive: false);
        var request = new CreateAssignmentRequest(EmployeeId, ClientId, Today, null, null);

        // Act
        var result = await service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A MIGRATION write may name an INACTIVE EDJEr. A person's write may not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves in one test on purpose: the exemption is only correct if it is narrow, and
    /// asserting that the migration succeeds without asserting that an ordinary caller still fails
    /// would pass just as happily if the guard had been deleted outright.
    /// </para>
    /// <para>
    /// Why it exists: a migration records an engagement that already happened rather than starting a
    /// new one. 528 of the 982 loadable assignments in the 2026-08-23 TPS delivery belong to people
    /// who have since left, and refusing them would discard more than half the assignment history
    /// while leaving the EDJEr rows in place — a directory showing 179 former colleagues who
    /// apparently never worked anywhere.
    /// </para>
    /// <para>
    /// Gating on <c>legacyTpsId</c> is safe because the endpoint runs
    /// <c>CompassLegacyProvenance.MaySet</c> first and REFUSES a non-migration caller who supplies
    /// one, so the value cannot be forged into this path from outside.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task CreateAsync_WithAnInactiveEdjer_IsAllowedForAMigrationWriteOnly()
    {
        // Arrange — the same inactive EDJEr in both cases; only provenance differs.
        var (migrationService, migrationRepo, _, _, _) = Build();
        migrationRepo.SeedEmployee(EmployeeId, isActive: false);
        migrationRepo.SeedClient(ClientId);

        var (personService, personRepo, _, _, _) = Build();
        personRepo.SeedEmployee(EmployeeId, isActive: false);
        personRepo.SeedClient(ClientId);

        // Act
        var migrated = await migrationService.CreateAsync(
            new CreateAssignmentRequest(
                EmployeeId,
                ClientId,
                Today,
                null,
                null,
                LegacyTpsId: "tps-assignment-08d6f1bd"
            ),
            TestContext.Current.CancellationToken
        );

        var byHand = await personService.CreateAsync(
            new CreateAssignmentRequest(EmployeeId, ClientId, Today, null, null),
            TestContext.Current.CancellationToken
        );

        // Assert
        migrated.Status.ShouldBe(
            AdminMutationStatus.Success,
            "a migration records history, and the EDJEr's having left does not unmake it"
        );

        byHand.Status.ShouldBe(
            AdminMutationStatus.ValidationError,
            "the exemption must not widen into a way for a person to assign someone who has left"
        );
    }

    /// <summary>
    /// The migration exemption waives ACTIVE, never EXISTS.
    /// </summary>
    /// <remarks>
    /// Without this, the exemption would turn an assignment naming no EDJEr at all into a
    /// foreign-key violation at <c>SaveChangesAsync</c> — an unhandled 500 mid-load rather than a
    /// named rejection in the grouped reject summary. An absent EDJEr is a dangling reference, not a
    /// historical fact.
    /// </remarks>
    [Fact]
    public async Task CreateAsync_MigrationWriteNamingNoEdjerAtAll_IsStillInvalid()
    {
        // Arrange — the client exists so that the EDJEr's absence is unambiguously what refuses this.
        var (service, repository, _, _, _) = Build();
        repository.SeedClient(ClientId);

        // Act
        var result = await service.CreateAsync(
            new CreateAssignmentRequest(
                EmployeeId,
                ClientId,
                Today,
                null,
                null,
                LegacyTpsId: "tps-assignment-08d6f1bd"
            ),
            TestContext.Current.CancellationToken
        );

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
    }

    [Fact]
    public async Task CreateAsync_WithAnUnknownEdjer_ReturnsInvalid()
    {
        // Arrange — no employee seeded at all.
        var (service, _, _, _, _) = Build();
        var request = new CreateAssignmentRequest(EmployeeId, ClientId, Today, null, null);

        // Act
        var result = await service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
    }

    [Fact]
    public async Task CreateAsync_WithAnUnknownClient_ReturnsInvalid()
    {
        // Arrange — the EDJEr exists and is active, but no client is seeded at all. Without this
        // check the invalid ClientId reaches SaveChangesAsync and trips the FK constraint on
        // client_assignment (23503), which CompassWriteFailure.IsTranslatable does not recognize.
        var (service, repository, _, _, _) = Build();
        repository.SeedEmployee(EmployeeId);
        var request = new CreateAssignmentRequest(EmployeeId, ClientId, Today, null, null);

        // Act
        var result = await service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    // ---------------------------------------------------------------- FR-008

    [Fact]
    public async Task CreateAsync_WithEndDateBeforeStartDate_ReturnsInvalid()
    {
        // Arrange — the CLIENT is seeded deliberately. Without it this request is refused one check
        // earlier ("The client must exist."), so the assertion passed while the date-order branch it
        // names was never reached — which is what the coverage report showed.
        var (service, repository, _, _, _) = Build();
        repository.SeedEmployee(EmployeeId);
        repository.SeedClient(ClientId);
        var request = new CreateAssignmentRequest(EmployeeId, ClientId, Today, Today.AddDays(-1), null);

        // Act
        var result = await service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert — the message, so a later reordering cannot satisfy this test with a different refusal.
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
        result.Error.ShouldBe("The end date must be on or after the start date.");
    }

    [Fact]
    public async Task CreateAsync_WithAnInvoiceFrequencyThatIsNoLongerSelectable_ReturnsInvalid()
    {
        // Arrange — FR-038/AC-26. The form offers active cadences only, and FR-041 says a hidden
        // option is never the control, so the service checks it too: a retired id must become a
        // message rather than a foreign-key violation surfacing as a 500.
        var (service, repository, _, _, _) = Build();
        repository.SeedEmployee(EmployeeId);
        repository.SeedClient(ClientId);
        var request = new CreateAssignmentRequest(
            EmployeeId, ClientId, Today, null, null, InvoiceFrequencyTypeId: 99);

        // Act
        var result = await service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
        result.Error.ShouldBe("That invoice frequency does not exist or is no longer selectable.");
    }

    [Fact]
    public async Task UpdateAsync_WithAnInvoiceFrequencyThatIsNoLongerSelectable_ReturnsInvalid()
    {
        // Arrange — the same rule on the edit surface, with the exemption that matters: an override
        // the assignment ALREADY carries is left alone even once its cadence is retired. This request
        // names a DIFFERENT id, so it is a selection and must satisfy FR-038.
        var (service, repository, _, _, _) = Build();
        repository.SeedEmployee(EmployeeId);
        var existing = repository.Seed(EmployeeId, ClientId, Today);

        // Act
        var result = await service.UpdateAsync(
            existing.Id,
            new UpdateAssignmentRequest(Today, null, null, InvoiceFrequencyTypeId: 99),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
        result.Error.ShouldBe("That invoice frequency does not exist or is no longer selectable.");
    }

    [Fact]
    public async Task UpdateAsync_ChangingTheInvoiceFrequencyOverride_RecordsItAsAFieldChange()
    {
        // Arrange — the audit entry is the record of WHAT changed, and an override is a billing
        // decision. A selectable cadence, so the write is accepted and the change is logged.
        var (service, repository, audit, _, _) = Build();
        repository.SeedEmployee(EmployeeId);
        repository.SelectableInvoiceFrequencyTypeIds.Add(3);
        var existing = repository.Seed(EmployeeId, ClientId, Today);

        // Act
        var result = await service.UpdateAsync(
            existing.Id,
            new UpdateAssignmentRequest(Today, null, null, InvoiceFrequencyTypeId: 3),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        var change = audit.Entries
            .ShouldHaveSingleItem()
            .Changes
            .ShouldHaveSingleItem();
        change.Field.ShouldBe("InvoiceFrequencyTypeId");
        change.Before.ShouldBeNull();
        change.After.ShouldBe("3");
    }

    [Fact]
    public async Task UpdateAsync_WithEndDateBeforeStartDate_ReturnsInvalid()
    {
        // Arrange
        var (service, repository, _, _, _) = Build();
        repository.SeedEmployee(EmployeeId);
        var existing = repository.Seed(EmployeeId, ClientId, Today);
        var request = new UpdateAssignmentRequest(Today, Today.AddDays(-1), null);

        // Act
        var result = await service.UpdateAsync(existing.Id, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
    }

    // ---------------------------------------------------------------- FR-004

    [Fact]
    public async Task UpdateAsync_WhenTheAssignmentDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var (service, _, _, _, _) = Build();
        var request = new UpdateAssignmentRequest(Today, null, null);

        // Act
        var result = await service.UpdateAsync(999_999, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.NotFound);
    }

    // ---------------------------------------------------------------- happy path + audit

    [Fact]
    public async Task CreateAsync_Succeeds_AndAuditsWithANonBlankReason()
    {
        // Arrange
        var (service, repository, audit, _, _) = Build();
        repository.SeedEmployee(EmployeeId);
        repository.SeedClient(ClientId);
        var request = new CreateAssignmentRequest(EmployeeId, ClientId, Today, null, "A note");

        // Act
        var result = await service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        result.Value.ShouldNotBeNull();
        result.Value!.Id.ShouldBeGreaterThan(0);

        audit.Entries.ShouldHaveSingleItem();
        var entry = audit.Entries[0];
        entry.EntityType.ShouldBe("CompassClientAssignment");
        entry.EntityId.ShouldBe(result.Value.Id.ToString());
        entry.Action.ShouldBe("create");
        entry.Reason.ShouldNotBeNullOrWhiteSpace();
        entry.EffectiveRoles.ShouldNotBeNull();
    }

    // ---------------------------------------------------------------- research R-4 translation

    [Fact]
    public async Task CreateAsync_WhenSaveLosesARaceOnATranslatableSqlState_ReturnsConflict()
    {
        // Arrange — the CHECK constraint fires on the save despite the service's own pre-check
        // (data-model §5, research R-4). CompassWriteFailure translates it into the same rejection
        // shape the pre-check returns, rather than an untranslated 500.
        var (service, repository, audit, _, unitOfWork) = Build();
        repository.SeedEmployee(EmployeeId);
        repository.SeedClient(ClientId);
        unitOfWork.ThrowOnSave = FailureWithSqlState(CompassWriteFailure.CheckViolationSqlState);
        var request = new CreateAssignmentRequest(EmployeeId, ClientId, Today, null, null);

        // Act
        var result = await service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
        result.Error.ShouldNotBeNullOrWhiteSpace();
        audit.Entries.ShouldBeEmpty("a failed save must never reach LogAsync");
    }

    [Fact]
    public async Task UpdateAsync_WhenLogAsyncLosesARaceOnATranslatableSqlState_ReturnsConflict()
    {
        // Arrange — for Update, IAuditService.LogAsync IS the commit (contract §5), so this is where
        // a lost race on the CHECK constraint actually surfaces.
        var (service, repository, audit, _, _) = Build();
        repository.SeedEmployee(EmployeeId);
        var existing = repository.Seed(EmployeeId, ClientId, Today);
        audit.ThrowOnLog = FailureWithSqlState(CompassWriteFailure.CheckViolationSqlState);
        var request = new UpdateAssignmentRequest(Today, Today.AddDays(30), null);

        // Act
        var result = await service.UpdateAsync(existing.Id, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task UpdateAsync_WhenTheEmployeesLastNameIsBlank_OmitsTheTrailingSeparatorFromEmployeeName()
    {
        // Arrange — compass.employee's name columns are non-nullable, not non-empty (PR #341
        // review): a blank surname must not leave a dangling "Ada " with a trailing space in the
        // row's EmployeeName.
        var (service, repository, _, _, _) = Build();
        repository.SeedEmployee(EmployeeId, lastName: string.Empty);
        var existing = repository.Seed(EmployeeId, ClientId, Today);

        // Act
        var result = await service.UpdateAsync(
            existing.Id,
            new UpdateAssignmentRequest(Today, null, null),
            TestContext.Current.CancellationToken);

        // Assert
        result.Value.ShouldNotBeNull();
        result.Value.EmployeeName.ShouldBe("Ada");
    }

    [Fact]
    public async Task UpdateAsync_ChangingTheStartDate_RecordsAFieldChange()
    {
        // Arrange
        var (service, repository, audit, _, _) = Build();
        repository.SeedEmployee(EmployeeId);
        var existing = repository.Seed(EmployeeId, ClientId, Today);
        var newStartDate = Today.AddDays(7);
        var request = new UpdateAssignmentRequest(newStartDate, null, null);

        // Act
        var result = await service.UpdateAsync(existing.Id, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        audit.Entries.ShouldHaveSingleItem();
        var change = audit.Entries[0].Changes.Single(c => c.Field == "StartDate");
        change.Before.ShouldBe(Today.ToString("O"));
        change.After.ShouldBe(newStartDate.ToString("O"));
    }

    [Fact]
    public async Task UpdateAsync_WithoutTouchingEndDate_AuditsAsUpdate()
    {
        // Arrange
        var (service, repository, audit, _, _) = Build();
        repository.SeedEmployee(EmployeeId);
        var existing = repository.Seed(EmployeeId, ClientId, Today);
        var request = new UpdateAssignmentRequest(Today, null, "Changed note");

        // Act
        var result = await service.UpdateAsync(existing.Id, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        audit.Entries.ShouldHaveSingleItem();
        audit.Entries[0].Action.ShouldBe("update");
    }

    [Fact]
    public async Task UpdateAsync_TheFirstNullToValueEndDateTransition_AuditsAsEnd()
    {
        // Arrange — J14 step 5: ending is a PUT, and the service detects the first transition.
        var (service, repository, audit, _, _) = Build();
        repository.SeedEmployee(EmployeeId);
        var existing = repository.Seed(EmployeeId, ClientId, Today);
        var request = new UpdateAssignmentRequest(Today, Today.AddDays(30), null);

        // Act
        var result = await service.UpdateAsync(existing.Id, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        audit.Entries.ShouldHaveSingleItem();
        audit.Entries[0].Action.ShouldBe("end");
    }

    [Fact]
    public async Task UpdateAsync_ChangingAnAlreadyEndDatedAssignment_AuditsAsUpdate_NotEndAgain()
    {
        // Arrange — FR-027's later-edit case: only the FIRST transition is "end".
        var (service, repository, audit, _, _) = Build();
        repository.SeedEmployee(EmployeeId);
        var existing = repository.Seed(EmployeeId, ClientId, Today, Today.AddDays(30));
        var request = new UpdateAssignmentRequest(Today, Today.AddDays(45), null);

        // Act
        var result = await service.UpdateAsync(existing.Id, request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        audit.Entries.ShouldHaveSingleItem();
        audit.Entries[0].Action.ShouldBe("update");
    }

    // ---------------------------------------------------------------- FR-005 / research R-1

    [Fact]
    public async Task GetAllAsync_DerivesIsCurrent_FromTheInjectedDerivation_NotLocally()
    {
        // Arrange — the double returns a stub predicate the REAL derivation would never produce.
        var (service, repository, _, derivation, _) = Build();
        repository.SeedEmployee(EmployeeId);
        repository.Seed(EmployeeId, ClientId, Today);

        // Act
        var rows = await service.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        derivation.IsCurrentWasCalled.ShouldBeTrue(
            "the service must ask IClientStatusDerivation for currency, never compute it itself");
        rows.ShouldHaveSingleItem();
        rows[0].IsCurrent.ShouldBeFalse(
            "IsCurrent must reflect the derivation's answer, even though this row has no end date and " +
            "the REAL rule would call that current — proving the value came from the double");
    }

    [Fact]
    public async Task CreateAsync_DerivesIsCurrent_FromTheInjectedDerivation_NotLocally()
    {
        // Arrange
        var (service, repository, _, derivation, _) = Build();
        repository.SeedEmployee(EmployeeId);
        repository.SeedClient(ClientId);
        var request = new CreateAssignmentRequest(EmployeeId, ClientId, Today, null, null);

        // Act
        var result = await service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        derivation.IsCurrentWasCalled.ShouldBeTrue();
        result.Value!.IsCurrent.ShouldBeFalse();
    }

    // ---------------------------------------------------------------- issue #593: DeleteAsync

    private static (
        CompassAssignmentService Service,
        FakeAssignmentRepository Repository,
        RecordingSowRepository Sows,
        RecordingAuditService Audit)
        BuildForDelete()
    {
        var repository = new FakeAssignmentRepository();
        var sows = new RecordingSowRepository();
        var unitOfWork = new FakeUnitOfWork(repository);
        var audit = new RecordingAuditService();
        var currentUser = new FakeCurrentUserContext(Guid.NewGuid(), "Compass Super Admin");
        var service = new CompassAssignmentService(
            repository, sows, unitOfWork, audit, currentUser,
            new RecordingClientStatusDerivation(), new FakeBusinessDate(Today), new RecordingCoachNotifier());

        return (service, repository, sows, audit);
    }

    [Fact]
    public async Task DeleteAsync_WhenTheAssignmentDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var (service, _, _, _) = BuildForDelete();

        // Act
        var status = await service.DeleteAsync(999_999, TestContext.Current.CancellationToken);

        // Assert
        status.ShouldBe(AdminMutationStatus.NotFound);
    }

    [Fact]
    public async Task DeleteAsync_WithNoSows_RemovesTheAssignment_AndAuditsWithANonBlankReason()
    {
        // Arrange
        var (service, repository, sows, audit) = BuildForDelete();
        repository.SeedEmployee(EmployeeId);
        var existing = repository.Seed(EmployeeId, ClientId, Today);

        // Act
        var status = await service.DeleteAsync(existing.Id, TestContext.Current.CancellationToken);

        // Assert
        status.ShouldBe(AdminMutationStatus.Success);
        (await repository.GetByIdAsync(existing.Id, TestContext.Current.CancellationToken)).ShouldBeNull();
        sows.Removed.ShouldBeEmpty();

        var entry = audit.Entries.ShouldHaveSingleItem();
        entry.EntityType.ShouldBe("CompassClientAssignment");
        entry.EntityId.ShouldBe(existing.Id.ToString());
        entry.Action.ShouldBe("delete");
        entry.Reason.ShouldNotBeNullOrWhiteSpace();
        entry.EffectiveRoles.ShouldNotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithSows_CascadesToEverySowUnderTheAssignment()
    {
        // Arrange — ekrumla's answer on issue #593: "if you delete an assignment, it should delete
        // all SOWs underneath it".
        var (service, repository, sows, audit) = BuildForDelete();
        repository.SeedEmployee(EmployeeId);
        var existing = repository.Seed(EmployeeId, ClientId, Today);
        var firstSow = new Sow { Id = 501, ClientAssignmentId = existing.Id };
        var secondSow = new Sow { Id = 502, ClientAssignmentId = existing.Id };
        existing.Sows.Add(firstSow);
        existing.Sows.Add(secondSow);

        // Act
        var status = await service.DeleteAsync(existing.Id, TestContext.Current.CancellationToken);

        // Assert
        status.ShouldBe(AdminMutationStatus.Success);
        sows.Removed.ShouldBe([firstSow, secondSow]);
        audit.Entries.ShouldHaveSingleItem().Changes
            .ShouldContain(c => c.Field == "SowCount" && c.Before == "2" && c.After == null);
    }
}
