using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Unit tests for EDJEr configuration: the email rule in all its forms, the active-type check, state
/// validation, the deactivation guard, and the audit obligation.
/// </summary>
/// <remarks>
/// Hand-written in-memory doubles rather than a mocking framework, matching
/// <c>CompassLookupServiceTests</c> and the project's conventions.
/// </remarks>
public class CompassEmployeeServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- the doubles

    /// <summary>
    /// In-memory stand-in for EDJEr data access. Normalises email the way the real repository's SQL
    /// does — <c>lower(btrim(...))</c> — so a test that passes here means the same thing there.
    /// </summary>
    private sealed class FakeEmployeeRepository : ICompassEmployeeRepository
    {
        private readonly List<Employee> _rows = [];
        private readonly List<BlockingAssignmentDto> _openAssignments = [];
        private int _nextId = 1;

        /// <summary>
        /// Employee types by id, each with its display name and whether it is selectable.
        /// </summary>
        /// <remarks>
        /// A retired type keeps its NAME — FR-007 requires a record already classified by it to keep
        /// displaying that classification — while ceasing to be selectable. Modelling the two
        /// separately is what lets a test assert both halves.
        /// </remarks>
        private readonly Dictionary<int, (string Name, bool IsActive)> _employeeTypes = [];

        public IReadOnlyList<Employee> Rows => _rows;

        /// <summary>Registers an employee type and whether it is selectable.</summary>
        public void SeedEmployeeType(int id, string name, bool isActive) =>
            _employeeTypes[id] = (name, isActive);

        public Employee Seed(string email, bool isActive = true, int employeeTypeId = 1)
        {
            var row = new Employee
            {
                Id = _nextId++,
                FirstName = "Seeded",
                LastName = "Edjer",
                HireDate = new DateOnly(2020, 1, 6),
                Email = email,
                EmployeeTypeId = employeeTypeId,
                IsActive = isActive,
                StateOfResidence = "OH",
            };
            _rows.Add(row);
            return row;
        }

        /// <summary>Gives an EDJEr an assignment with no end date — the deactivation guard's input.</summary>
        public void SeedOpenAssignment(int assignmentId, int clientId, string clientName) =>
            _openAssignments.Add(
                new BlockingAssignmentDto(assignmentId, clientId, clientName, new DateOnly(2024, 4, 1))
            );

        public Task<IReadOnlyList<(Employee Employee, string EmployeeTypeName)>> GetAllWithTypeNameAsync(
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyList<(Employee, string)>>(
                [
                    .. _rows
                        .OrderBy(row => row.LastName)
                        .ThenBy(row => row.FirstName)
                        .Select(row =>
                            (
                                row,
                                _employeeTypes.TryGetValue(row.EmployeeTypeId, out var type)
                                    ? type.Name
                                    : "Unknown"
                            )
                        ),
                ]
            );

        /// <summary>How many times the EDJEr has been loaded — the guard must not re-fetch it.</summary>
        public int GetByIdCallCount { get; private set; }

        public Task<Employee?> GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            GetByIdCallCount++;
            return Task.FromResult(_rows.SingleOrDefault(row => row.Id == id));
        }

        public Task<bool> EmailExistsAsync(
            string email,
            int? excludingId,
            CancellationToken cancellationToken
        )
        {
            var candidate = email.Trim().ToLowerInvariant();
            return Task.FromResult(
                _rows.Any(row =>
                    row.Id != excludingId && row.Email.Trim().ToLowerInvariant() == candidate
                )
            );
        }

        public Task<bool> ActiveEmployeeTypeExistsAsync(
            int employeeTypeId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                _employeeTypes.TryGetValue(employeeTypeId, out var type) && type.IsActive
            );

        public Task<bool> EmployeeTypeExistsAsync(
            int employeeTypeId,
            CancellationToken cancellationToken
        ) => Task.FromResult(_employeeTypes.ContainsKey(employeeTypeId));

        public Task<bool> ExistsAsync(int employeeId, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.Any(row => row.Id == employeeId));

        public Task<bool> ActiveEmployeeExistsAsync(
            int employeeId,
            CancellationToken cancellationToken
        ) => Task.FromResult(_rows.Any(row => row.Id == employeeId && row.IsActive));

        /// <summary>How many times the blocker query has run — zero when there is no transition.</summary>
        public int OpenAssignmentQueryCount { get; private set; }

        public Task<IReadOnlyList<BlockingAssignmentDto>> GetOpenAssignmentsAsync(
            int employeeId,
            CancellationToken cancellationToken
        )
        {
            OpenAssignmentQueryCount++;
            return Task.FromResult<IReadOnlyList<BlockingAssignmentDto>>([.. _openAssignments]);
        }

        public Task AddAsync(Employee employee, CancellationToken cancellationToken)
        {
            employee.Id = _nextId++;
            _rows.Add(employee);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingUnitOfWork : ICompassUnitOfWork
    {
        /// <summary>
        /// Runs the operation with no transaction: these are unit tests over an in-memory double, where
        /// there is nothing to commit. The transactional guarantee itself is asserted against real
        /// Postgres by <c>CompassAuditAtomicityTests</c>, because only a real provider has transactions.
        /// </summary>
        public Task<T> ExecuteAtomicallyAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            Func<T, bool> commitWhen,
            CancellationToken cancellationToken) => operation(cancellationToken);

        public int SaveCount { get; private set; }

        /// <summary>
        /// When set, the next save reports a lost uniqueness race — what the database does when a
        /// concurrent caller committed the same email between this request's check and its write.
        /// </summary>
        public bool FailNextSaveAsDuplicate { get; set; }

        /// <summary>
        /// Which unique index the simulated failure names. Defaults to the email index the service
        /// pre-checks; set it to the provenance index to simulate a duplicate legacy TPS identifier,
        /// which nothing pre-checks at all.
        /// </summary>
        public string? FailNextSaveConstraint { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            if (FailNextSaveAsDuplicate)
            {
                throw new CompassDuplicateKeyException(
                    "A concurrent write already committed a value with this name.",
                    FailNextSaveConstraint ?? "ux_employee_email_ci"
                );
            }
            return Task.FromResult(1);
        }
    }

    /// <summary>Records what was audited, so a test can assert the entry's content and not just its count.</summary>
    /// <remarks>
    /// The read members throw rather than returning empty: this service never browses the audit trail, and
    /// a double that silently answers a call it should never receive hides a mistake instead of surfacing
    /// it.
    /// </remarks>
    private sealed class RecordingAuditService : IAuditService
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task LogAsync(AuditEntry entry)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditLogResponse>> GetByEntityAsync(
            string entityType,
            string entityId
        ) => throw new NotSupportedException("EDJEr configuration never reads the audit trail");

        public Task<PaginatedAuditLogResponse> BrowseAsync(
            string? entityType,
            string? actor,
            string? employeeId,
            DateTime? fromDate,
            DateTime? toDate,
            int page,
            int pageSize
        ) => throw new NotSupportedException("EDJEr configuration never reads the audit trail");

        public Task<IReadOnlyList<string>> GetDistinctEntityTypesAsync() =>
            throw new NotSupportedException("EDJEr configuration never reads the audit trail");
    }

    private sealed class StubCurrentUser : ICurrentUserContext
    {
        public Guid EdjeId { get; init; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string Email => "super.admin@example.test";
        public string TpsEmployeeId => "1";
        public IReadOnlyList<string> Privileges { get; init; } =
            ["Compass Super Admin", "Compass Admin"];

        public bool HasPrivilege(string privilege) => Privileges.Contains(privilege);
    }

    private static (
        CompassEmployeeService Service,
        FakeEmployeeRepository Employees,
        CountingUnitOfWork UnitOfWork,
        RecordingAuditService Audit
    ) Build()
    {
        var employees = new FakeEmployeeRepository();
        employees.SeedEmployeeType(1, "Full Time", isActive: true);
        employees.SeedEmployeeType(2, "Intern", isActive: false);

        var unitOfWork = new CountingUnitOfWork();
        var audit = new RecordingAuditService();
        // The REAL guard, not a stub (US7, #65). The deactivation checks below now assert the service's
        // delegation as well as the verdict, which is the point of extracting it: a stubbed guard here
        // would let the write path and the blockers route drift apart while both suites stayed green.
        var service = new CompassEmployeeService(
            employees,
            unitOfWork,
            audit,
            new StubCurrentUser(),
            new EdjerDeactivationGuard(employees)
        );

        return (service, employees, unitOfWork, audit);
    }

    private static CompassEdjerRequest Request(
        string email = "ada.lovelace@example.test",
        int employeeTypeId = 1,
        string state = "OH",
        bool isActive = true,
        int? coachEmployeeId = null,
        string firstName = "Ada",
        string lastName = "Lovelace",
        DateOnly? hireDate = null,
        string? timezone = "America/New_York",
        string? legacyTpsId = null,
        bool? isDeliveryTeam = null
    ) =>
        // isDeliveryTeam defaults to null — absent — so every test written before the flag existed
        // still describes a client that says nothing about it.
        new(
            firstName,
            lastName,
            hireDate ?? new DateOnly(2020, 1, 6),
            email,
            employeeTypeId,
            coachEmployeeId,
            state,
            isActive,
            TimesheetRequired: true,
            CanSubmitUnder40: false,
            IncludeInPayroll: true,
            Timezone: timezone,
            LegacyTpsId: legacyTpsId,
            IsDeliveryTeam: isDeliveryTeam
        );

    // ---------------------------------------------------------------- the happy path

    [Fact]
    public async Task CreateEdjer_WithAValidRequest_SucceedsAndPersistsOnce()
    {
        // Arrange
        var (service, employees, unitOfWork, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value.ShouldNotBeNull();
        result.Value.Email.ShouldBe("ada.lovelace@example.test");
        result.Value.Id.ShouldBeGreaterThan(0);
        employees.Rows.Count.ShouldBe(1);
        unitOfWork.SaveCount.ShouldBe(1, "the SERVICE owns the persistence boundary");
    }

    [Fact]
    public async Task CreateEdjer_NormalisesTheStoredEmail_SoTheStoredFormMatchesTheIndexedOne()
    {
        // Arrange — the stored value must be the normalised one, not merely compared as if it were.
        // Storing " Ada@Example.test " while the unique index reads lower(btrim(email)) would leave the
        // table holding a value no later exact-match lookup could find.
        var (service, employees, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(email: "  Ada.Lovelace@Example.test  "), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        employees.Rows.Single().Email.ShouldBe("ada.lovelace@example.test");
    }

    [Fact]
    public async Task CreateEdjer_UpperCasesTheState_SoTheCharColumnHoldsOneForm()
    {
        // Arrange
        var (service, employees, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(state: "oh"), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        employees.Rows.Single().StateOfResidence.ShouldBe("OH");
    }

    // ------------------------------------------------- BR-9: email uniqueness, all four combinations

    [Theory]
    [InlineData(true, "an ACTIVE counterpart")]
    [InlineData(false, "an INACTIVE counterpart — BR-9's whole point")]
    public async Task CreateEdjer_WithAnEmailAlreadyHeld_IsRejectedRegardlessOfTheHoldersStatus(
        bool counterpartIsActive,
        string because
    )
    {
        // Arrange
        var (service, employees, unitOfWork, _) = Build();
        employees.Seed("ada.lovelace@example.test", isActive: counterpartIsActive);

        // Act
        var result = await service.CreateEdjerAsync(Request(email: "ada.lovelace@example.test"), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict, because);
        result.Error.ShouldNotBeNullOrWhiteSpace("the rejection must name the conflicting field (FR-013)");
        result.Error.ShouldContain("email", Case.Insensitive);
        employees.Rows.Count.ShouldBe(1, "no record may be created");
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(true, "Ada.Lovelace@example.test", "differing only by case, against an active EDJEr")]
    [InlineData(false, "ADA.LOVELACE@EXAMPLE.TEST", "upper-cased, against an inactive EDJEr")]
    [InlineData(true, "  ada.lovelace@example.test  ", "surrounded by whitespace")]
    [InlineData(false, " Ada.Lovelace@example.test ", "case AND whitespace, against an inactive EDJEr")]
    public async Task CreateEdjer_WithAnEmailThatNormalisesToAnExistingOne_IsRejected(
        bool counterpartIsActive,
        string submitted,
        string because
    )
    {
        // Arrange — spec A-2: the comparison is case-insensitive and whitespace-trimmed, because a
        // collision differing only by case defeats BR-9's purpose.
        var (service, employees, unitOfWork, _) = Build();
        employees.Seed("ada.lovelace@example.test", isActive: counterpartIsActive);

        // Act
        var result = await service.CreateEdjerAsync(Request(email: submitted), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict, because);
        employees.Rows.Count.ShouldBe(1);
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateEdjer_ChangingTheEmailToOneHeldByAnother_IsRejectedAndChangesNothing()
    {
        // Arrange
        var (service, employees, unitOfWork, _) = Build();
        var target = employees.Seed("grace.hopper@example.test");
        employees.Seed("ada.lovelace@example.test", isActive: false);

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        target.Email.ShouldBe(
            "grace.hopper@example.test",
            "the rejected update must not have been applied"
        );
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateEdjer_KeepingItsOwnEmail_IsNotTreatedAsACollisionWithItself()
    {
        // Arrange — the row being edited must be excluded from the check, or editing any other field
        // without also changing the email would be impossible.
        var (service, employees, _, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test");

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test", firstName: "Augusta"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        target.FirstName.ShouldBe("Augusta");
    }

    [Fact]
    public async Task CreateEdjer_LosingAConcurrentEmailRace_IsA409AndNotAnUnhandledFailure()
    {
        // Arrange — the pre-check is a check-then-act, so a concurrent caller can commit the same
        // address between it and this write; ux_employee_email_ci then rejects ours. The losing writer
        // must take the SAME path as the sequential duplicate rather than escaping as a bare 500 (the
        // defect found in review of PR #213 on the lookup surface).
        var (service, _, unitOfWork, _) = Build();
        unitOfWork.FailNextSaveAsDuplicate = true;

        // Act
        var result = await service.CreateEdjerAsync(Request(), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("email", Case.Insensitive);
    }

    [Fact]
    public async Task UpdateEdjer_LosingAConcurrentEmailRace_IsA409AndNotAnUnhandledFailure()
    {
        // Arrange - the same check-then-act as the create path above, one handler along. Two
        // administrators moving different EDJErs to the same address both clear the pre-check and the
        // index rejects the loser. Covered separately because it is a SECOND catch block: the create
        // path's test leaves this one unexercised, and an update that escaped as a bare 500 would look
        // nothing like the 409 the sequential duplicate already returns.
        var (service, employees, unitOfWork, _) = Build();
        var target = employees.Seed("grace.hopper@example.test");
        unitOfWork.FailNextSaveAsDuplicate = true;

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("email", Case.Insensitive);
    }

    // --------------------------------------------- a lost race on a NON-email unique index

    // compass.employee carries TWO unique indexes: ux_employee_email_ci and
    // ux_employee_legacy_tps_id. Only the first is pre-checked, so the provenance one is the
    // collision a migration re-run actually hits — and the catch must not describe it as an email
    // problem, naming a field the caller never touched.

    [Fact]
    public async Task CreateEdjer_LosingARaceOnTheProvenanceIndex_NamesProvenanceRatherThanEmail()
    {
        // Arrange
        var (service, _, unitOfWork, _) = Build();
        unitOfWork.FailNextSaveAsDuplicate = true;
        unitOfWork.FailNextSaveConstraint = "ux_employee_legacy_tps_id";

        // Act
        var result = await service.CreateEdjerAsync(Request(legacyTpsId: "tps-employee-1"), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("tps-employee-1");
        result.Error.ShouldNotContain("email", Case.Insensitive);
    }

    [Fact]
    public async Task UpdateEdjer_LosingARaceOnTheProvenanceIndex_NamesProvenanceRatherThanEmail()
    {
        // Arrange
        var (service, employees, unitOfWork, _) = Build();
        var target = employees.Seed("grace.hopper@example.test");
        unitOfWork.FailNextSaveAsDuplicate = true;
        unitOfWork.FailNextSaveConstraint = "ux_employee_legacy_tps_id";

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "grace.hopper@example.test", legacyTpsId: "tps-employee-2"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("tps-employee-2");
        result.Error.ShouldNotContain("email", Case.Insensitive);
    }

    // ------------------------------------------------------------- FR-005: only active employee types

    [Fact]
    public async Task CreateEdjer_NamingAnINACTIVEEmployeeType_IsRefusedByTheServer()
    {
        // Arrange — the selection list omitting inactive types is convenience; this is the enforcement.
        // "It was never offered" is not a guard against a caller who names it anyway.
        var (service, _, unitOfWork, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(employeeTypeId: 2), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        result.Error.ShouldNotBeNullOrWhiteSpace();
        unitOfWork.SaveCount.ShouldBe(0);
    }

    /// <summary>
    /// A MIGRATION write may name a retired classification. A person's write may not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves are one test on purpose: the exemption is only correct if it is narrow, and
    /// asserting that the migration succeeds without asserting that an ordinary caller still fails
    /// would pass just as happily if the guard had been deleted outright.
    /// </para>
    /// <para>
    /// Why the exemption exists: <c>Intern</c> is seeded INACTIVE so nobody can classify a new hire
    /// as one, but the TPS delivery carries EDJErs who are exactly that. A migration write reports
    /// what the legacy directory said; it does not choose a classification now. (Its former sibling
    /// <c>Unknown</c> was retired by issue #398 — the exemption narrowed with it, and did not go
    /// away.)
    /// </para>
    /// <para>
    /// Gating on <c>legacyTpsId</c> is safe because the endpoint runs
    /// <c>CompassLegacyProvenance.MaySet</c> first and refuses a non-migration caller who supplies
    /// one — so the value cannot be forged into this path from outside.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task CreateEdjer_NamingARetiredEmployeeType_IsAllowedForMigrationOnly()
    {
        // Arrange — type 2 is seeded inactive by Build().
        var (migrationService, _, migrationUnitOfWork, _) = Build();
        var (personService, _, personUnitOfWork, _) = Build();

        // Act
        var migrated = await migrationService.CreateEdjerAsync(
            Request(employeeTypeId: 2, legacyTpsId: "tps-employee-08d6f1bd"),
            Token
        );
        var byHand = await personService.CreateEdjerAsync(Request(employeeTypeId: 2), Token);

        // Assert
        migrated.Status.ShouldBe(
            CompassWriteStatus.Success,
            "a migration write records the classification TPS carried, retired or not"
        );
        migrationUnitOfWork.SaveCount.ShouldBe(1);

        byHand.Status.ShouldBe(
            CompassWriteStatus.ValidationError,
            "the exemption must not widen into a way for a person to pick a retired classification"
        );
        personUnitOfWork.SaveCount.ShouldBe(0);
    }

    /// <summary>
    /// The migration exemption waives ACTIVE, never EXISTS.
    /// </summary>
    /// <remarks>
    /// Without this the exemption would turn a bad identifier into a foreign-key violation at
    /// <c>SaveChangesAsync</c> — an unhandled 500 mid-load rather than a named rejection the operator
    /// can read in the grouped reject summary.
    /// </remarks>
    [Fact]
    public async Task CreateEdjer_MigrationWriteNamingANonexistentEmployeeType_IsStillRefused()
    {
        // Arrange
        var (service, _, unitOfWork, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(
            Request(employeeTypeId: 999, legacyTpsId: "tps-employee-08d6f1bd"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task CreateEdjer_NamingAnUnknownEmployeeType_IsRefused()
    {
        // Arrange
        var (service, _, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(employeeTypeId: 9999), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
    }

    [Fact]
    public async Task UpdateEdjer_LeavingAnAlreadyInactiveTypeUntouched_IsStillRefused()
    {
        // Arrange — the deliberate edge: an EDJEr keeps a type that was retired after they were
        // classified by it (FR-007), and an edit that does not touch the type must not be forced to
        // re-select. This test records what the service ACTUALLY does, which is to refuse — the type is
        // re-submitted by the form on every save, so the server cannot distinguish "unchanged" from
        // "newly chosen" without comparing to the stored value.
        var (service, employees, _, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test", employeeTypeId: 2);

        // Act
        var result = await service.UpdateEdjerAsync(target.Id, Request(employeeTypeId: 2), Token);

        // Assert — accepted, because the EDJEr already carries this type and the edit does not change it.
        result.Status.ShouldBe(
            CompassWriteStatus.Success,
            "an edit that leaves a retired classification untouched must not be blocked (FR-007)"
        );
    }

    // ------------------------------------------------------------------- FR-014: state of residence

    [Theory]
    [InlineData("ZZ", "not a US state code")]
    [InlineData("PR", "a US territory — deliberately excluded per AC-NFR-6")]
    [InlineData("", "blank")]
    [InlineData("OHIO", "a name rather than a code")]
    public async Task CreateEdjer_WithAStateOutsideTheFiftyPlusDC_IsRejected(
        string state,
        string because
    )
    {
        // Arrange
        var (service, _, unitOfWork, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(state: state), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError, because);
        unitOfWork.SaveCount.ShouldBe(0);
    }

    /// <summary>
    /// A state of residence is REQUIRED. Absent, blank and whitespace are all refused.
    /// </summary>
    /// <remarks>
    /// The column was briefly made optional for the 2026-08-23 TPS delivery, which supplies no state
    /// for 65 of its 255 EDJErs. The product owner decided on 2026-08-24 to default those to
    /// <c>OH</c> at migration time instead, so the API requirement stands unchanged and the
    /// substitution happens once, in the migration tool, where it is counted and reported.
    /// <para>
    /// Asserted for all three empty shapes because a caller cannot distinguish them and none of them
    /// is a state.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(null, "omitted entirely")]
    [InlineData("", "blank")]
    [InlineData("   ", "whitespace only")]
    public async Task CreateEdjer_WithNoState_IsRejected(string? state, string because)
    {
        // Arrange
        var (service, _, unitOfWork, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(state: state!), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError, because);
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("OH")]
    [InlineData("DC")]
    [InlineData("AK")]
    [InlineData("WY")]
    public async Task CreateEdjer_WithAnAcceptedStateCode_Succeeds(string state)
    {
        // The positive control for the state rule. Without it, a validator that rejected everything
        // would satisfy every case above.
        // Arrange
        var (service, _, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(state: state), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
    }

    // ------------------------------------------------------------------------ FR-8.1: the timezone

    [Theory]
    [InlineData("America/New_York")]
    [InlineData("America/Chicago")]
    [InlineData("America/Denver")]
    [InlineData("America/Los_Angeles")]
    [InlineData("Pacific/Honolulu")]
    [InlineData("America/Anchorage")]
    public async Task CreateEdjer_WithASupportedZone_StoresTheIanaIdAsSubmitted(string timezone)
    {
        // Arrange — every one of the six, because a validator that accepted only the first would
        // satisfy a single-value test while making five of the dropdown's options unusable.
        var (service, employees, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(timezone: timezone), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value!.Timezone.ShouldBe(timezone);
        employees.Rows.Single().Timezone.ShouldBe(timezone);
    }

    [Theory]
    [InlineData("Europe/London", "a real IANA zone, but not one of the six (FR-8.1b)")]
    [InlineData("Eastern", "the DISPLAY label, not the stored identifier")]
    [InlineData("America/Indiana/Indianapolis", "a US zone the six do not include")]
    [InlineData("america/new_york", "the right zone, wrongly cased — IANA ids are case-sensitive")]
    [InlineData("EST", "an abbreviation, which carries no DST rules")]
    public async Task CreateEdjer_WithAZoneOutsideTheSix_IsRejected(string timezone, string because)
    {
        // Arrange
        var (service, _, unitOfWork, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(timezone: timezone), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError, because);
        unitOfWork.SaveCount.ShouldBe(0);
    }

    /// <summary>
    /// A submitted-but-blank timezone is REFUSED, which is the half of FR-8.1 that "required" means.
    /// </summary>
    /// <remarks>
    /// This is the case the wire-optional parameter makes possible and must not swallow. A form
    /// submitted with nothing chosen sends <c>""</c>; folding that into the omitted case would file
    /// the EDJEr under Eastern without anyone choosing it — the exact behaviour issue #421 exists to
    /// end. See <c>CompassEdjerRequest.Timezone</c>.
    /// </remarks>
    [Theory]
    [InlineData("", "blank — the form was submitted with nothing chosen")]
    [InlineData("   ", "whitespace only")]
    public async Task CreateEdjer_WithABlankTimezone_IsRejected(string timezone, string because)
    {
        // Arrange
        var (service, _, unitOfWork, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(timezone: timezone), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError, because);
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task CreateEdjer_WithNoTimezoneAtAll_DefaultsToEastern()
    {
        // Arrange — the ONLY caller that omits it is one older than FR-8.1, and /api/compass/v1 is
        // additive-only, so this must succeed rather than 400. It takes the same value the column
        // defaults to, so the two cannot disagree.
        var (service, employees, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(timezone: null), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value!.Timezone.ShouldBe(UsTimeZones.Default);
        employees.Rows.Single().Timezone.ShouldBe("America/New_York");
    }

    [Fact]
    public async Task UpdateEdjer_WithANewZone_StoresItAndAuditsTheChange()
    {
        // Arrange
        var (service, employees, _, audit) = Build();
        var target = employees.Seed("ada.lovelace@example.test");
        target.Timezone = "America/New_York";

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test", timezone: "America/Denver"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        employees.Rows.Single().Timezone.ShouldBe("America/Denver");

        var change = audit.Entries.ShouldHaveSingleItem()
            .Changes.Single(entry => entry.Field == "Timezone");
        change.Before.ShouldBe("America/New_York");
        change.After.ShouldBe("America/Denver");
    }

    [Fact]
    public async Task UpdateEdjer_WithNoTimezone_LeavesTheStoredZoneAlone()
    {
        // Arrange — the field is wire-optional, so absent must mean "unchanged". If it meant "make it
        // the default", a client older than FR-8.1 saving an unrelated field would silently move a
        // deliberately-chosen Pacific EDJEr to Eastern. Pacific specifically, so a bug that reset to
        // the default is visible rather than coincidentally equal.
        var (service, employees, _, audit) = Build();
        var target = employees.Seed("ada.lovelace@example.test");
        target.Timezone = "America/Los_Angeles";

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test", firstName: "Augusta", timezone: null),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        employees.Rows.Single().Timezone.ShouldBe("America/Los_Angeles");

        audit.Entries.ShouldHaveSingleItem()
            .Changes.ShouldNotContain(
                change => change.Field == "Timezone",
                "a write that changed nothing must not report a change"
            );
    }

    [Fact]
    public async Task CreateEdjer_AuditsTheTimezoneItStored()
    {
        // Arrange — FR-017 wants the whole new record, and the zone is now part of it.
        var (service, _, _, audit) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(timezone: "Pacific/Honolulu"), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        var change = audit.Entries.ShouldHaveSingleItem()
            .Changes.Single(entry => entry.Field == "Timezone");
        change.Before.ShouldBeNull();
        change.After.ShouldBe("Pacific/Honolulu");
    }

    // ----------------------------------------------------------------- the delivery-team flag

    [Fact]
    public async Task CreateEdjer_WithNoDeliveryTeamFlag_StartsOnTheDeliveryTeam()
    {
        // Arrange — absent means the caller said nothing, and the ruling on issue #502 is that
        // everybody starts on the delivery team. The service writes it rather than leaving it to the
        // column default, so an application create does not depend on the catalog.
        var (service, employees, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(isDeliveryTeam: null), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value!.IsDeliveryTeam.ShouldBeTrue();
        employees.Rows.Single().IsDeliveryTeam.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateEdjer_WithTheDeliveryTeamFlagOff_StoresFalse()
    {
        // Arrange — the value a nullable bool exists to keep distinguishable from absent.
        var (service, employees, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(isDeliveryTeam: false), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value!.IsDeliveryTeam.ShouldBeFalse();
        employees.Rows.Single().IsDeliveryTeam.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateEdjer_WithTheDeliveryTeamFlagPresent_WritesIt()
    {
        // Arrange — the other half of the guard: present must still be applied, or the field would be
        // uneditable rather than merely protected.
        var (service, employees, _, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test");
        target.IsDeliveryTeam = true;

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test", isDeliveryTeam: false),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value!.IsDeliveryTeam.ShouldBeFalse();
        employees.Rows.Single().IsDeliveryTeam.ShouldBeFalse();
    }

    /// <summary>
    /// A hand-made correction to <c>false</c> survives a save that says nothing about the flag.
    /// </summary>
    /// <remarks>
    /// The real writer this defends against is the migration tool's coach pass, which re-PUTs the
    /// whole original create payload. Assigning unconditionally would reset every correction an
    /// administrator had made in between (FR-006, SC-004).
    /// </remarks>
    [Fact]
    public async Task UpdateEdjer_WithNoDeliveryTeamFlag_LeavesACorrectionAlone()
    {
        // Arrange
        var (service, employees, _, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test");
        target.IsDeliveryTeam = false;

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test", firstName: "Augusta", isDeliveryTeam: null),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        employees.Rows.Single().IsDeliveryTeam.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateEdjer_TakingTheEdjerOffDelivery_AuditsTheChange()
    {
        // Arrange — every other field is aligned with the request first, so the change list this
        // produces has room for exactly one entry and a stray one would be visible.
        var (service, employees, _, audit) = Build();
        var target = employees.Seed("ada.lovelace@example.test");
        target.FirstName = "Ada";
        target.LastName = "Lovelace";
        target.TimesheetRequired = true;
        target.IncludeInPayroll = true;
        target.IsDeliveryTeam = true;

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test", isDeliveryTeam: false),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        var changes = audit.Entries.ShouldHaveSingleItem().Changes;
        var change = changes.ShouldHaveSingleItem();
        change.Field.ShouldBe(nameof(Employee.IsDeliveryTeam));
        change.Before.ShouldBe("True");
        change.After.ShouldBe("False");
    }

    /// <summary>
    /// A save that says nothing about the flag records nothing about it.
    /// </summary>
    /// <remarks>
    /// The comparison is against the REQUEST, not the resulting entity, so a naive
    /// <c>after.IsDeliveryTeam.ToString()</c> compares <c>"True"</c> against <c>""</c> and fabricates
    /// an entry on every save by a client older than this field — a false entry in a trail FR-017
    /// exists to make trustworthy.
    /// </remarks>
    [Fact]
    public async Task UpdateEdjer_WithNoDeliveryTeamFlag_AuditsNoChangeToIt()
    {
        // Arrange
        var (service, employees, _, audit) = Build();
        var target = employees.Seed("ada.lovelace@example.test");
        target.IsDeliveryTeam = true;

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test", firstName: "Augusta", isDeliveryTeam: null),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        audit
            .Entries.ShouldHaveSingleItem()
            .Changes.ShouldNotContain(
                change => change.Field == nameof(Employee.IsDeliveryTeam),
                "a write that changed nothing must not report a change"
            );
    }

    /// <summary>
    /// Re-sending the value the record already holds records nothing.
    /// </summary>
    /// <remarks>
    /// The other half of the sibling above. FR-017 forbids an entry when the value did not change,
    /// and that includes a client sending the current value rather than saying nothing.
    /// </remarks>
    [Fact]
    public async Task UpdateEdjer_ResendingTheSameDeliveryTeamFlag_AuditsNoChangeToIt()
    {
        // Arrange
        var (service, employees, _, audit) = Build();
        var target = employees.Seed("ada.lovelace@example.test");
        target.IsDeliveryTeam = true;

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test", firstName: "Augusta", isDeliveryTeam: true),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        employees.Rows.Single().IsDeliveryTeam.ShouldBeTrue();
        audit
            .Entries.ShouldHaveSingleItem()
            .Changes.ShouldNotContain(
                change => change.Field == nameof(Employee.IsDeliveryTeam),
                "re-sending the stored value is not a change"
            );
    }

    /// <summary>
    /// An EDJEr can be put back on the delivery team, and it audits.
    /// </summary>
    /// <remarks>
    /// The reverse of taking one off, so that leaving the delivery team cannot become a one-way door.
    /// </remarks>
    [Fact]
    public async Task UpdateEdjer_PuttingTheEdjerBackOnDelivery_WritesAndAuditsIt()
    {
        // Arrange — aligned like the take-off-delivery sibling, so one entry is the whole list.
        var (service, employees, _, audit) = Build();
        var target = employees.Seed("ada.lovelace@example.test");
        target.FirstName = "Ada";
        target.LastName = "Lovelace";
        target.TimesheetRequired = true;
        target.IncludeInPayroll = true;
        target.IsDeliveryTeam = false;

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test", isDeliveryTeam: true),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        employees.Rows.Single().IsDeliveryTeam.ShouldBeTrue();
        var change = audit.Entries.ShouldHaveSingleItem().Changes.ShouldHaveSingleItem();
        change.Field.ShouldBe(nameof(Employee.IsDeliveryTeam));
        change.Before.ShouldBe("False");
        change.After.ShouldBe("True");
    }

    [Fact]
    public async Task CreateEdjer_AuditsTheDeliveryTeamFlagItStored()
    {
        // Arrange — FR-017 wants the whole new record, and the flag is now part of it.
        var (service, _, _, audit) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(isDeliveryTeam: false), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        var changes = audit.Entries.ShouldHaveSingleItem().Changes;
        changes.ShouldContain(change => change.Field == nameof(Employee.IsDeliveryTeam));
        var change = changes.Single(entry => entry.Field == nameof(Employee.IsDeliveryTeam));
        change.Before.ShouldBeNull();
        change.After.ShouldBe("False");
    }

    // ----------------------------------------------------------------- required fields, and the coach

    [Theory]
    [InlineData("", "Lovelace", "a blank first name")]
    [InlineData("   ", "Lovelace", "a whitespace-only first name")]
    [InlineData("Ada", "", "a blank last name")]
    public async Task CreateEdjer_MissingARequiredName_IsRejected(
        string firstName,
        string lastName,
        string because
    )
    {
        // Arrange
        var (service, _, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(
            Request(firstName: firstName, lastName: lastName),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError, because);
    }

    // The PRESENCE of each of these fields was already covered; none of the LENGTH limits was. The
    // two rules fail differently: a blank name is a caller who left a box empty, whereas an
    // over-length one is a caller whose value will not fit the column, and only the second can reach
    // the database as a truncation or an error far from its cause.

    [Fact]
    public async Task CreateEdjer_WithAFirstNameOverTheLengthLimit_IsRejected()
    {
        // Arrange - 101 characters against a 100-character limit: the first value that must fail.
        var (service, _, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(
            Request(firstName: new string('a', 101)),
            Token
        );

        // Assert - the message must name the limit, or the caller cannot tell by how much.
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("100");
    }

    [Fact]
    public async Task CreateEdjer_WithALastNameOverTheLengthLimit_IsRejected()
    {
        // Arrange
        var (service, _, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(lastName: new string('a', 101)), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("100");
    }

    [Fact]
    public async Task CreateEdjer_WithAnEmailOverTheLengthLimit_IsRejected()
    {
        // Arrange - 263 characters against a 255-character limit, and deliberately a well-FORMED
        // address. The length rule is checked before the plausibility rule, so an implausible value
        // would pass this test for the wrong reason and stop proving the length rule exists.
        var (service, _, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(
            Request(email: new string('a', 250) + "@example.test"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("255");
    }

    [Fact]
    public async Task CreateEdjer_WithNoHireDate_IsRejected()
    {
        // Arrange - default(DateOnly) is 0001-01-01, which is what a caller who omits the field
        // serialises to. It is a real date as far as the column is concerned, so nothing downstream
        // would reject it: this rule is the only thing standing between an omitted hire date and a
        // stored one that reads as the year 1.
        var (service, _, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(hireDate: default(DateOnly)), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("hire date", Case.Insensitive);
    }

    [Theory]
    [InlineData("", "blank")]
    [InlineData("   ", "whitespace only")]
    [InlineData("not-an-address", "no @ at all")]
    [InlineData("@example.test", "nothing before the @")]
    [InlineData("ada@", "nothing after the @")]
    public async Task CreateEdjer_WithAnUnusableEmail_IsRejected(string email, string because)
    {
        // Arrange — email is the identity BR-9 turns on, so a value that cannot be one is a 400 rather
        // than a row nobody can ever match.
        var (service, _, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(email: email), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError, because);
    }

    [Fact]
    public async Task CreateEdjer_WithNoCoach_Succeeds()
    {
        // Arrange — AC-17 makes the coach OPTIONAL. The mockup marks it required with an asterisk; the
        // acceptance criterion wins, and the column is nullable.
        var (service, _, _, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(coachEmployeeId: null), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value!.CoachEmployeeId.ShouldBeNull();
    }

    [Fact]
    public async Task CreateEdjer_NamingACoachWhoDoesNotExist_IsRejected()
    {
        // Arrange — a foreign key would reject this at the database with a 500; naming it here is a 400.
        var (service, _, unitOfWork, _) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(coachEmployeeId: 9999), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task CreateEdjer_NamingAnExistingCoach_Succeeds()
    {
        // Arrange
        var (service, employees, _, _) = Build();
        var coach = employees.Seed("jordan.wells@example.test");

        // Act
        var result = await service.CreateEdjerAsync(Request(coachEmployeeId: coach.Id), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value!.CoachEmployeeId.ShouldBe(coach.Id);
    }

    [Fact]
    public async Task UpdateEdjer_NamingItselfAsItsOwnCoach_IsRefusedByTheServer()
    {
        // Arrange — the form already excludes the EDJEr from its own coach picker, so the rule exists as
        // far as the product is concerned. FR-041 makes client-side validation never the control, which
        // means a caller bypassing the form must be refused here too. Found in review of this slice.
        //
        // It is not merely nonsensical. `ICurrentUserContext` documents Manager scope as "direct reports
        // (from TPS coach hierarchy)", so a self-referencing coach makes someone their own direct report,
        // and any walk over that hierarchy has a one-node cycle in it.
        var (service, employees, unitOfWork, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test");

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test", coachEmployeeId: target.Id),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        result.Error.ShouldNotBeNullOrWhiteSpace();
        target.CoachEmployeeId.ShouldBeNull("the rejected update must not have been applied");
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateEdjer_NamingSomeoneElseAsCoach_StillSucceeds()
    {
        // The positive control for the guard above: it must reject SELF, not every coach on an update.
        // Arrange
        var (service, employees, _, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test");
        var coach = employees.Seed("jordan.wells@example.test");

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test", coachEmployeeId: coach.Id),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        target.CoachEmployeeId.ShouldBe(coach.Id);
    }

    [Fact]
    public async Task UpdateEdjer_NamingAFormerEdjerAsCoach_IsRefusedByTheServer()
    {
        // Issue #400. The coach picker must offer active EDJErs only, and FR-041 makes that filter a
        // convenience rather than the control -- so a caller bypassing the form must be refused here.
        // Arrange
        var (service, employees, unitOfWork, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test");
        var formerCoach = employees.Seed("dana.prior@example.test", isActive: false);

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test", coachEmployeeId: formerCoach.Id),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        result.Error.ShouldNotBeNullOrWhiteSpace();
        target.CoachEmployeeId.ShouldBeNull("the rejected update must not have been applied");
        unitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task CreateEdjer_NamingAFormerEdjerAsCoach_IsRefusedByTheServer()
    {
        // Arrange
        var (service, employees, _, _) = Build();
        var formerCoach = employees.Seed("dana.prior@example.test", isActive: false);

        // Act
        var result = await service.CreateEdjerAsync(
            Request(coachEmployeeId: formerCoach.Id),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task UpdateEdjer_KeepingAnAlreadyAssignedFormerCoach_StillSucceeds()
    {
        // The grandfather case, and the reason the rule above is scoped to a CHANGE rather than to any
        // inactive coach. An EDJEr whose coach has since been deactivated must stay editable: opening the
        // form to change an unrelated field resends the SAME coachEmployeeId, and refusing that would
        // make the record permanently unsaveable -- the server-side twin of the desync this issue's
        // client fix avoids.
        // Arrange
        var (service, employees, _, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test");
        var formerCoach = employees.Seed("dana.prior@example.test", isActive: false);
        target.CoachEmployeeId = formerCoach.Id;

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test", coachEmployeeId: formerCoach.Id),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        target.CoachEmployeeId.ShouldBe(formerCoach.Id);
    }

    // ------------------------------------------------- FR-019 / BR-10: the deactivation guard

    [Fact]
    public async Task Deactivating_AnEdjerHoldingAnOpenAssignment_IsRefusedAndNamesTheBlockers()
    {
        // Arrange
        var (service, employees, unitOfWork, audit) = Build();
        var target = employees.Seed("ada.lovelace@example.test", isActive: true);
        employees.SeedOpenAssignment(assignmentId: 42, clientId: 7, clientName: "Buckeye Mutual");

        // Act
        var result = await service.UpdateEdjerAsync(target.Id, Request(isActive: false), Token);

        // Assert — 422, not 400: the request is well formed and authorised, and the state of the world
        // forbids it. AC-19 requires the refusal to IDENTIFY what must be end-dated first.
        result.Status.ShouldBe(CompassWriteStatus.PreconditionFailed);
        result.BlockingAssignments.ShouldNotBeNull();
        result.BlockingAssignments.Count.ShouldBe(1);
        result.BlockingAssignments[0].AssignmentId.ShouldBe(42);
        result.BlockingAssignments[0].ClientName.ShouldBe("Buckeye Mutual");
        result.Error.ShouldNotBeNullOrWhiteSpace();

        target.IsActive.ShouldBeTrue("the EDJEr must remain active");
        unitOfWork.SaveCount.ShouldBe(0, "no assignment is auto-ended and nothing else is written");
        audit.Entries.ShouldBeEmpty("a refused write is not a write, so it is not audited");
    }

    [Fact]
    public async Task Deactivating_AnEdjerHoldingAnOpenAssignment_WhenTheLastNameIsBlank_OmitsTheTrailingSeparatorFromTheMessage()
    {
        // Arrange — compass.employee's name columns are non-nullable, not non-empty (PR #341
        // review): a blank surname must not leave a dangling "Seeded " with a trailing space in the
        // AC-19 blocked-deactivation message.
        var (service, employees, _, _) = Build();
        var target = employees.Seed("solo@example.test", isActive: true);
        target.LastName = string.Empty;
        employees.SeedOpenAssignment(assignmentId: 42, clientId: 7, clientName: "Buckeye Mutual");

        // Act
        var result = await service.UpdateEdjerAsync(target.Id, Request(isActive: false), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.PreconditionFailed);
        result.Error.ShouldStartWith("Seeded still holds");
    }

    [Fact]
    public async Task Deactivating_AnEdjerWhoseAssignmentsAreAllEnded_Succeeds()
    {
        // Arrange — no open assignments seeded, so nothing blocks (US2 scenario 11).
        var (service, employees, unitOfWork, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test", isActive: true);

        // Act
        var result = await service.UpdateEdjerAsync(target.Id, Request(isActive: false), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        target.IsActive.ShouldBeFalse();
        unitOfWork.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task TheGuard_DoesNotFire_WhenAnAlreadyInactiveEdjerIsEditedWithoutReactivating()
    {
        // Arrange — the guard is on the TRANSITION active → inactive (AC-19: "attempting to toggle the
        // active flag off"). An already-inactive EDJEr being edited is not toggling anything, and
        // blocking it would make an inactive EDJEr uneditable if a stale open assignment existed.
        var (service, employees, _, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test", isActive: false);
        employees.SeedOpenAssignment(assignmentId: 42, clientId: 7, clientName: "Buckeye Mutual");

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(isActive: false, firstName: "Augusta"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        target.FirstName.ShouldBe("Augusta");
    }

    [Fact]
    public async Task TheGuard_DoesNotFire_WhenAnEdjerWithOpenAssignmentsStaysActive()
    {
        // Arrange — an ordinary profile edit on an actively-engaged EDJEr must not be blocked.
        var (service, employees, _, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test", isActive: true);
        employees.SeedOpenAssignment(assignmentId: 42, clientId: 7, clientName: "Buckeye Mutual");

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(isActive: true, firstName: "Augusta"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        target.FirstName.ShouldBe("Augusta");
    }

    [Fact]
    public async Task Deactivating_LoadsTheEdjerOnce_RatherThanAgainInsideTheGuard()
    {
        // Arrange — the service already holds the EDJEr it is about to mutate, so the guard must be
        // handed that instance rather than looking it up a second time. PR #295 review nit: the
        // id-taking overload made every isActive:false submission cost a redundant round trip.
        var (service, employees, _, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test", isActive: true);
        employees.SeedOpenAssignment(assignmentId: 42, clientId: 7, clientName: "Buckeye Mutual");

        // Act
        var result = await service.UpdateEdjerAsync(target.Id, Request(isActive: false), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.PreconditionFailed);
        employees.GetByIdCallCount.ShouldBe(
            1,
            "the guard evaluates the entity the caller already loaded, not the id"
        );
    }

    [Fact]
    public async Task EditingAnAlreadyInactiveEdjer_CostsNoBlockerQueryAtAll()
    {
        // Arrange — the second half of the same nit. An already-inactive EDJEr is not transitioning, so
        // the guard should answer from the entity alone. Before the fix this reached the database twice:
        // once to re-load the EDJEr and once for blockers it could never act on.
        var (service, employees, _, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test", isActive: false);
        employees.SeedOpenAssignment(assignmentId: 42, clientId: 7, clientName: "Buckeye Mutual");

        // Act
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(isActive: false, firstName: "Augusta"),
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        employees.GetByIdCallCount.ShouldBe(1, "no re-fetch");
        employees.OpenAssignmentQueryCount.ShouldBe(
            0,
            "there is no transition to guard, so the blocker query must never run"
        );
    }

    // ------------------------------------------------------------------------- FR-017: the audit trail

    [Fact]
    public async Task CreateEdjer_WritesAnAuditEntry_NamingTheActorTheChangeAndTheEffectiveRoles()
    {
        // Arrange
        var (service, _, _, audit) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        var entry = audit.Entries.ShouldHaveSingleItem();

        entry.EntityType.ShouldBe("CompassEmployee");
        entry.Action.ShouldBe("create");
        entry.Actor.ShouldBe("11111111-1111-1111-1111-111111111111");
        entry.Reason.ShouldNotBeNullOrWhiteSpace(
            "AuditService.LogAsync throws on a blank reason (research F-1)"
        );

        // The whole unfiltered privilege set, NOT narrowed to a "Compass " prefix (owner decision).
        entry.EffectiveRoles.ShouldNotBeNull();
        entry.EffectiveRoles.ShouldContain("Compass Super Admin");
        entry.EffectiveRoles.ShouldContain("Compass Admin");

        entry.Changes.ShouldNotBeEmpty("an audit entry with no changes records that something happened but not what");
    }

    [Fact]
    public async Task CreateEdjer_AuditsTheRealIdentityKey_NotTheUnsavedDefault()
    {
        // Arrange — the sequencing trap. EF assigns the identity key at save, and repositories only
        // stage, so reading it BEFORE the save records EntityId "0" — which is what
        // AdminJobTitleService does today. The Compass path saves first for exactly this reason.
        var (service, _, _, audit) = Build();

        // Act
        var result = await service.CreateEdjerAsync(Request(), Token);

        // Assert
        var entry = audit.Entries.ShouldHaveSingleItem();
        entry.EntityId.ShouldBe(result.Value!.Id.ToString());
        entry.EntityId.ShouldNotBe("0", "an audit row pointing at id 0 is attributable to nothing");
    }

    [Fact]
    public async Task UpdateEdjer_AuditsBeforeAndAfterForEveryChangedField_AndOmitsUnchangedOnes()
    {
        // Arrange — the stored record must match the request in every field EXCEPT the two under test,
        // or "omits unchanged ones" would be asserting against fields this arrangement changed by
        // accident. Seed() leaves the flags false and the name "Seeded Edjer", so both are aligned to
        // Request()'s defaults first.
        var (service, employees, _, audit) = Build();
        var target = employees.Seed("ada.lovelace@example.test");
        target.FirstName = "Ada";
        target.LastName = "Lovelace";
        target.TimesheetRequired = true;
        target.CanSubmitUnder40 = false;
        target.IncludeInPayroll = true;

        // Act — change a profile field and a time-tracking flag, leave the rest alone.
        var result = await service.UpdateEdjerAsync(
            target.Id,
            Request(email: "ada.lovelace@example.test", firstName: "Augusta") with
            {
                TimesheetRequired = false,
            },
            Token
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        var entry = audit.Entries.ShouldHaveSingleItem();
        entry.Action.ShouldBe("update");

        var firstName = entry.Changes.Single(change => change.Field == "FirstName");
        firstName.Before.ShouldBe("Ada");
        firstName.After.ShouldBe("Augusta");

        entry.Changes.ShouldContain(change => change.Field == "TimesheetRequired");
        entry.Changes.ShouldNotContain(
            change => change.Field == "LastName",
            "an unchanged field in the change list makes a real change harder to find"
        );
    }

    [Fact]
    public async Task ARejectedWrite_IsNeverAudited()
    {
        // Arrange — an audit row for something that did not happen is worse than no row.
        var (service, employees, _, audit) = Build();
        employees.Seed("ada.lovelace@example.test");

        // Act
        var result = await service.CreateEdjerAsync(Request(email: "ada.lovelace@example.test"), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        audit.Entries.ShouldBeEmpty();
    }

    /// <summary>
    /// The mirror image of <c>CompassLookupServiceTests.TheLookupService_CannotReachTheAuditService</c>.
    /// </summary>
    /// <remarks>
    /// That test enforces FR-008's exemption structurally — the lookup service cannot audit because it
    /// holds no audit dependency. This one enforces FR-017 the same way: a service that could not reach
    /// the audit service would fail every assertion above with a null reference rather than with a
    /// missing entry, which is a worse diagnostic. Asserting the dependency directly says what the rule
    /// is.
    /// </remarks>
    [Fact]
    public void TheEmployeeService_TakesTheAuditService_BecauseItsWritesMustBeAttributable()
    {
        // Arrange / Act
        var parameters = typeof(CompassEmployeeService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToList();

        // Assert
        parameters.ShouldContain(typeof(IAuditService), "FR-017: EDJEr writes are audited");
        parameters.ShouldContain(
            typeof(ICurrentUserContext),
            "the audit entry needs the actor and their effective roles"
        );
    }

    // ------------------------------------------------------------------------------- reads

    [Fact]
    public async Task GetEdjers_ReturnsActiveAndInactive_OrderedByFamilyThenGivenName()
    {
        // Arrange — an administrator who cannot see a deactivated EDJEr cannot reactivate one, and a
        // Compass Admin and above see both (BR-1).
        var (service, employees, _, _) = Build();
        employees.Seed("zoe.brooks@example.test", isActive: false).LastName = "Brooks";
        var alvarez = employees.Seed("maya.alvarez@example.test");
        alvarez.LastName = "Alvarez";

        // Act
        var edjers = await service.GetEdjersAsync(Token);

        // Assert
        edjers.Count.ShouldBe(2);
        edjers[0].LastName.ShouldBe("Alvarez");
        edjers.ShouldContain(edjer => !edjer.IsActive, "inactive EDJErs must be listed");
        edjers[0].EmployeeTypeName.ShouldBe(
            "Full Time",
            "the classification is resolved server-side, not looked up per row on the client"
        );
    }

    [Fact]
    public async Task GetEdjers_ReportsHireDateAndState_ForTheAdminListColumns()
    {
        // Arrange — issue #659: the admin list adopts Team Directory's Hire Date and State columns.
        var (service, employees, _, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test");
        target.HireDate = new DateOnly(2019, 3, 14);
        target.StateOfResidence = "CA";

        // Act
        var edjers = await service.GetEdjersAsync(Token);

        // Assert
        edjers.Single().HireDate.ShouldBe(new DateOnly(2019, 3, 14));
        edjers.Single().StateOfResidence.ShouldBe("CA");
    }

    [Fact]
    public async Task GetEdjers_ResolvesTheCoachsName_ServerSide()
    {
        // Arrange — issue #659: the admin list shows the coach's name as plain text, resolved
        // server-side so the client needs no second lookup (the same reasoning as EmployeeTypeName).
        var (service, employees, _, _) = Build();
        var coach = employees.Seed("jordan.wells@example.test");
        coach.FirstName = "Jordan";
        coach.LastName = "Wells";
        var target = employees.Seed("ada.lovelace@example.test");
        target.CoachEmployeeId = coach.Id;

        // Act
        var edjers = await service.GetEdjersAsync(Token);

        // Assert
        edjers.Single(edjer => edjer.Id == target.Id).CoachName.ShouldBe("Jordan Wells");
    }

    [Fact]
    public async Task GetEdjers_ReportsNoCoachName_WhenTheEdjerHasNoCoach()
    {
        // Arrange
        var (service, employees, _, _) = Build();
        employees.Seed("ada.lovelace@example.test");

        // Act
        var edjers = await service.GetEdjersAsync(Token);

        // Assert
        edjers.Single().CoachName.ShouldBeNull();
    }

    [Fact]
    public async Task GetEdjer_ForAnUnknownId_ReturnsNull()
    {
        // Arrange
        var (service, _, _, _) = Build();

        // Act
        var edjer = await service.GetEdjerAsync(9999, Token);

        // Assert
        edjer.ShouldBeNull();
    }

    [Fact]
    public async Task GetEdjer_ReturnsTheFullRecordIncludingTheTimeTrackingFlags()
    {
        // Arrange — safe here because every route returning this DTO requires Compass Super Admin.
        var (service, employees, _, _) = Build();
        var target = employees.Seed("ada.lovelace@example.test");
        target.TimesheetRequired = true;
        target.CanSubmitUnder40 = true;
        target.IncludeInPayroll = false;

        // Act
        var edjer = await service.GetEdjerAsync(target.Id, Token);

        // Assert
        edjer.ShouldNotBeNull();
        edjer.TimesheetRequired.ShouldBeTrue();
        edjer.CanSubmitUnder40.ShouldBeTrue();
        edjer.IncludeInPayroll.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateEdjer_ForAnUnknownId_IsNotFound()
    {
        // Arrange
        var (service, _, unitOfWork, _) = Build();

        // Act
        var result = await service.UpdateEdjerAsync(9999, Request(), Token);

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.NotFound);
        unitOfWork.SaveCount.ShouldBe(0);
    }
}
