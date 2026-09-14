using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// The AC-19 deactivation guard's verdict in each of its four states (US7/#65, T124).
/// </summary>
/// <remarks>
/// <para>
/// Hand-written in-memory doubles rather than a mocking framework, matching
/// <c>CompassEmployeeServiceTests</c> and the project's conventions.
/// </para>
/// <para>
/// FR-045 — a future end date must not block — is NOT asserted here, deliberately. The guard
/// never compares dates; it asks the repository for assignments whose <c>end_date</c> is NULL, and a
/// future end date is simply not NULL. Asserting it against a double would only prove the double
/// filters the way the double was written. It is asserted against real PostgreSQL in
/// <c>tests/integration/Endpoints/CompassDeactivationGuardTests</c>, where the NULL is a real NULL.
/// </para>
/// </remarks>
public class EdjerDeactivationGuardTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- the double

    /// <summary>
    /// In-memory stand-in for EDJEr data access, keyed per EDJEr.
    /// </summary>
    /// <remarks>
    /// Keyed by employee id rather than holding one flat list, unlike
    /// <c>CompassEmployeeServiceTests</c>'s equivalent: the guard's contract is "this EDJEr's
    /// blockers", and a double that returns every seeded assignment regardless of who was asked about
    /// cannot fail when the guard ignores its argument.
    /// </remarks>
    private sealed class FakeEmployeeRepository : ICompassEmployeeRepository
    {
        private readonly List<Employee> _rows = [];
        private readonly Dictionary<int, List<BlockingAssignmentDto>> _openAssignments = [];
        private int _nextId = 1;

        public Employee Seed(bool isActive)
        {
            var row = new Employee
            {
                Id = _nextId++,
                FirstName = "Ada",
                LastName = "Lovelace",
                HireDate = new DateOnly(2020, 1, 6),
                Email = $"ada.{_nextId}@example.test",
                EmployeeTypeId = 1,
                IsActive = isActive,
                StateOfResidence = "OH",
            };
            _rows.Add(row);
            return row;
        }

        /// <summary>Gives one EDJEr an assignment with no end date — the guard's input.</summary>
        public void SeedOpenAssignment(int employeeId, int assignmentId, string clientName)
        {
            if (!_openAssignments.TryGetValue(employeeId, out var forEmployee))
            {
                forEmployee = [];
                _openAssignments[employeeId] = forEmployee;
            }

            forEmployee.Add(
                new BlockingAssignmentDto(assignmentId, 7, clientName, new DateOnly(2024, 4, 1))
            );
        }

        public Task<IReadOnlyList<BlockingAssignmentDto>> GetOpenAssignmentsAsync(
            int employeeId,
            CancellationToken cancellationToken
        )
        {
            OpenAssignmentQueryCount++;
            return Task.FromResult<IReadOnlyList<BlockingAssignmentDto>>(
                _openAssignments.TryGetValue(employeeId, out var forEmployee) ? [.. forEmployee] : []
            );
        }

        /// <summary>How many times an EDJEr was looked up by id — zero for the entity overload.</summary>
        public int GetByIdCallCount { get; private set; }

        /// <summary>How many times the blocker query ran — zero when there is no transition.</summary>
        public int OpenAssignmentQueryCount { get; private set; }

        public Task<Employee?> GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            GetByIdCallCount++;
            return Task.FromResult(_rows.SingleOrDefault(row => row.Id == id));
        }

        // ---- Not reached by the guard. It asks two questions and no more (FR-042: it never mutates).

        public Task<IReadOnlyList<(Employee Employee, string EmployeeTypeName)>> GetAllWithTypeNameAsync(
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<bool> EmailExistsAsync(
            string email,
            int? excludingId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<bool> ActiveEmployeeTypeExistsAsync(
            int employeeTypeId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<bool> EmployeeTypeExistsAsync(
            int employeeTypeId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<bool> ExistsAsync(int employeeId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ActiveEmployeeExistsAsync(
            int employeeId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task AddAsync(Employee employee, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static (EdjerDeactivationGuard Guard, FakeEmployeeRepository Employees) Build()
    {
        var employees = new FakeEmployeeRepository();
        return (new EdjerDeactivationGuard(employees), employees);
    }

    // ---------------------------------------------------------------- the four states

    [Fact]
    public async Task Evaluate_UnknownEdjer_IsNotFound()
    {
        // Arrange
        var (guard, _) = Build();

        // Act
        var verdict = await guard.EvaluateAsync(404, Token);

        // Assert
        verdict.Status.ShouldBe(EdjerDeactivationStatus.NotFound);
        verdict.BlockingAssignments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Evaluate_AnAlreadyInactiveEdjer_IsAlreadyInactive()
    {
        // Arrange — AC-19 guards the transition active to inactive. An EDJEr who is already inactive is
        // not toggling anything, so the guard reports the absence of a transition rather than permitting
        // one. Reporting Permitted here would tell the edit form a deactivation is available when there
        // is nothing left to deactivate.
        var (guard, employees) = Build();
        var target = employees.Seed(isActive: false);
        employees.SeedOpenAssignment(target.Id, assignmentId: 42, clientName: "Buckeye Mutual");

        // Act
        var verdict = await guard.EvaluateAsync(target.Id, Token);

        // Assert
        verdict.Status.ShouldBe(EdjerDeactivationStatus.AlreadyInactive);
        verdict.BlockingAssignments.ShouldBeEmpty(
            "there is no transition to block, so there is nothing to report"
        );
    }

    [Fact]
    public async Task Evaluate_AnActiveEdjerWithNoOpenAssignments_IsPermitted()
    {
        // Arrange — FR-044.
        var (guard, employees) = Build();
        var target = employees.Seed(isActive: true);

        // Act
        var verdict = await guard.EvaluateAsync(target.Id, Token);

        // Assert
        verdict.Status.ShouldBe(EdjerDeactivationStatus.Permitted);
        verdict.BlockingAssignments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Evaluate_AnActiveEdjerHoldingAnOpenAssignment_IsBlockedAndNamesIt()
    {
        // Arrange — FR-040, FR-041. The refusal must IDENTIFY what to end-date, so the client's name
        // travels with it: an assignment id alone is not actionable by a human.
        var (guard, employees) = Build();
        var target = employees.Seed(isActive: true);
        employees.SeedOpenAssignment(target.Id, assignmentId: 42, clientName: "Buckeye Mutual");

        // Act
        var verdict = await guard.EvaluateAsync(target.Id, Token);

        // Assert
        verdict.Status.ShouldBe(EdjerDeactivationStatus.Blocked);
        verdict.BlockingAssignments.Count.ShouldBe(1);
        verdict.BlockingAssignments[0].AssignmentId.ShouldBe(42);
        verdict.BlockingAssignments[0].ClientName.ShouldBe("Buckeye Mutual");
    }

    // ------------------------------------------------- the entity overload (PR #295 review nit)

    [Fact]
    public async Task Evaluate_GivenAnAlreadyLoadedEdjer_DoesNotLookThemUpAgain()
    {
        // Arrange — the write path holds the EDJEr before it decides anything, so re-reading the same
        // row there is a round trip that buys nothing.
        var (guard, employees) = Build();
        var target = employees.Seed(isActive: true);
        employees.SeedOpenAssignment(target.Id, assignmentId: 42, clientName: "Buckeye Mutual");

        // Act
        var verdict = await guard.EvaluateAsync(target, Token);

        // Assert
        verdict.Status.ShouldBe(EdjerDeactivationStatus.Blocked);
        verdict.BlockingAssignments.Count.ShouldBe(1);
        employees.GetByIdCallCount.ShouldBe(0, "the caller already had the EDJEr");
    }

    [Fact]
    public async Task Evaluate_GivenAnAlreadyInactiveEdjer_RunsNoBlockerQuery()
    {
        // Arrange — there is no transition to guard, so the blockers are not merely irrelevant, they
        // must not be fetched: it is a query whose result nobody may act on.
        var (guard, employees) = Build();
        var target = employees.Seed(isActive: false);
        employees.SeedOpenAssignment(target.Id, assignmentId: 42, clientName: "Buckeye Mutual");

        // Act
        var verdict = await guard.EvaluateAsync(target, Token);

        // Assert
        verdict.Status.ShouldBe(EdjerDeactivationStatus.AlreadyInactive);
        employees.OpenAssignmentQueryCount.ShouldBe(0);
    }

    [Fact]
    public async Task Evaluate_ByIdAndByEntity_AgreeOnTheSameEdjer()
    {
        // Arrange — one derivation is the whole point of this service, so the two entry points must not
        // be allowed to drift into two answers.
        var (guard, employees) = Build();
        var target = employees.Seed(isActive: true);
        employees.SeedOpenAssignment(target.Id, assignmentId: 42, clientName: "Buckeye Mutual");

        // Act
        var byId = await guard.EvaluateAsync(target.Id, Token);
        var byEntity = await guard.EvaluateAsync(target, Token);

        // Assert
        byEntity.Status.ShouldBe(byId.Status);
        byEntity.BlockingAssignments.Count.ShouldBe(byId.BlockingAssignments.Count);
    }

    // ---------------------------------------------------------------- the guard's own boundaries

    [Fact]
    public async Task Evaluate_ReportsOnlyTheSubjectsBlockers_NotAnotherEdjers()
    {
        // Arrange — two EDJErs, only one engaged. A guard that ignored its argument would refuse the
        // wrong person, and every single-EDJEr test above would still pass.
        var (guard, employees) = Build();
        var engaged = employees.Seed(isActive: true);
        var unengaged = employees.Seed(isActive: true);
        employees.SeedOpenAssignment(engaged.Id, assignmentId: 42, clientName: "Buckeye Mutual");

        // Act
        var verdict = await guard.EvaluateAsync(unengaged.Id, Token);

        // Assert
        verdict.Status.ShouldBe(EdjerDeactivationStatus.Permitted);
    }

    [Fact]
    public async Task Evaluate_LeavesTheEdjerAndItsBlockersUntouched()
    {
        // Arrange — FR-042: no assignment is ever auto-ended, and evaluating is not deciding. The
        // repository double throws on every method beyond the two reads, so reaching for anything else
        // fails the test rather than passing quietly.
        var (guard, employees) = Build();
        var target = employees.Seed(isActive: true);
        employees.SeedOpenAssignment(target.Id, assignmentId: 42, clientName: "Buckeye Mutual");

        // Act
        await guard.EvaluateAsync(target.Id, Token);

        // Assert
        target.IsActive.ShouldBeTrue("evaluating a precondition must not act on it");
        var second = await guard.EvaluateAsync(target.Id, Token);
        second.Status.ShouldBe(
            EdjerDeactivationStatus.Blocked,
            "the blocker must still be there — evaluation does not consume it"
        );
    }
}
