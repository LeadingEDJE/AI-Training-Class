using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

public class UserRoleServiceTests : IDisposable
{
    private readonly IUserRoleService _service;
    private readonly InMemoryUserRoleRepository _repository;
    private readonly LeapDbContext _context;
    private readonly Guid _testEdjeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _otherEdjeId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public UserRoleServiceTests()
    {
        _repository = new InMemoryUserRoleRepository();
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new LeapDbContext(options);

        // Use a stub audit service that does nothing
        var auditService = new StubAuditService();
        _service = new UserRoleService(_repository, auditService, _context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task GetRolesAsync_ReturnsAllRolesForUser()
    {
        // Arrange
        await _repository.AddAsync(new UserRole { EdjeId = _testEdjeId, Role = "EDJEr" });
        await _repository.AddAsync(new UserRole { EdjeId = _testEdjeId, Role = "Manager" });
        await _repository.AddAsync(new UserRole { EdjeId = _otherEdjeId, Role = "Admin" });

        // Act
        var result = await _service.GetRolesAsync(_testEdjeId);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldContain("EDJEr");
        result.ShouldContain("Manager");
    }

    [Fact]
    public async Task HasRoleAsync_UserHasRole_ReturnsTrue()
    {
        // Arrange
        await _repository.AddAsync(new UserRole { EdjeId = _testEdjeId, Role = "EDJEr" });

        // Act
        var result = await _service.HasRoleAsync(_testEdjeId, "EDJEr");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task HasRoleAsync_UserDoesNotHaveRole_ReturnsFalse()
    {
        // Arrange
        await _repository.AddAsync(new UserRole { EdjeId = _testEdjeId, Role = "EDJEr" });

        // Act
        var result = await _service.HasRoleAsync(_testEdjeId, "SuperAdmin");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task HasAnyRoleAsync_UserHasOneOfRoles_ReturnsTrue()
    {
        // Arrange
        await _repository.AddAsync(new UserRole { EdjeId = _testEdjeId, Role = "Manager" });

        // Act
        var result = await _service.HasAnyRoleAsync(_testEdjeId, "Manager", "TimesheetProcessor", "SuperAdmin");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task HasAnyRoleAsync_UserHasNoneOfRoles_ReturnsFalse()
    {
        // Arrange
        await _repository.AddAsync(new UserRole { EdjeId = _testEdjeId, Role = "EDJEr" });

        // Act
        var result = await _service.HasAnyRoleAsync(_testEdjeId, "Manager", "TimesheetProcessor", "SuperAdmin");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task AssignRoleAsync_NewRole_AddsRole()
    {
        // Act
        await _service.AssignRoleAsync(_testEdjeId, "EDJEr", "admin@test.com", "Initial assignment");

        // Assert
        var roles = await _service.GetRolesAsync(_testEdjeId);
        roles.ShouldContain("EDJEr");
    }

    [Fact]
    public async Task AssignRoleAsync_DuplicateRole_IsIdempotent()
    {
        // Arrange
        await _service.AssignRoleAsync(_testEdjeId, "EDJEr", "admin@test.com", "First assignment");

        // Act
        await _service.AssignRoleAsync(_testEdjeId, "EDJEr", "admin@test.com", "Second assignment");

        // Assert
        var roles = await _service.GetRolesAsync(_testEdjeId);
        roles.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AssignRoleAsync_ConcurrentInsertWinsTheRace_DoesNotThrow()
    {
        // AssignRoleAsync is a check-then-act: FindAsync says "not assigned",
        // then AddAsync + SaveChangesAsync inserts. When a concurrent request
        // commits the SAME (EdjeId, Role) inside that window, Postgres rejects
        // our INSERT with SQLSTATE 23505 on ix_user_roles_edje_id_role and EF
        // surfaces it as a DbUpdateException from SaveChangesAsync.
        //
        // The method is documented as idempotent, so the losing writer must
        // treat that as "someone already assigned it" and complete normally --
        // not bubble a 500 out of POST /api/admin/user-roles.

        // Arrange: FindAsync misses (row not yet committed), SaveChanges then
        // raises the unique violation -- exactly the production ordering.
        await using var context = new DuplicateKeyThrowingDbContext(
            new DbContextOptionsBuilder<LeapDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options);
        var service = new UserRoleService(
            new InMemoryUserRoleRepository(), new StubAuditService(), context);

        // Act + Assert: must not throw
        await service.AssignRoleAsync(_testEdjeId, "EDJEr", "admin@test.com", "Concurrent assignment");
    }

    [Fact]
    public async Task AssignRoleAsync_NonDuplicateDbUpdateException_Bubbles()
    {
        // Guardrail on the fix above: only SQLSTATE 23505 is benign. A genuine
        // failure (FK violation, deadlock, disk error) must still surface, so
        // the catch cannot be a blanket swallow.

        // Arrange
        await using var context = new GenericDbUpdateThrowingDbContext(
            new DbContextOptionsBuilder<LeapDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options);
        var service = new UserRoleService(
            new InMemoryUserRoleRepository(), new StubAuditService(), context);

        // Act + Assert
        await Should.ThrowAsync<DbUpdateException>(
            () => service.AssignRoleAsync(_testEdjeId, "EDJEr", "admin@test.com", "Assignment"));
    }

    /// <summary>
    /// DbContext stub whose SaveChangesAsync raises the Postgres unique-violation
    /// (SQLSTATE 23505) EF surfaces when a concurrent request commits the same
    /// (EdjeId, Role) between our FindAsync and our INSERT.
    /// </summary>
    private class DuplicateKeyThrowingDbContext(DbContextOptions<LeapDbContext> options)
        : LeapDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateException(
                "An error occurred while saving the entity changes.",
                new PostgresException(
                    "duplicate key value violates unique constraint \"ix_user_roles_edje_id_role\"",
                    "ERROR", "ERROR", "23505"));
    }

    /// <summary>
    /// DbContext stub whose SaveChangesAsync raises a NON-duplicate-key
    /// <see cref="DbUpdateException"/>, so the duplicate-tolerance in
    /// AssignRoleAsync can be proven not to swallow real failures.
    /// </summary>
    private class GenericDbUpdateThrowingDbContext(DbContextOptions<LeapDbContext> options)
        : LeapDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateException(
                "An error occurred while saving the entity changes.",
                new PostgresException(
                    "insert or update on table \"user_roles\" violates foreign key constraint",
                    "ERROR", "ERROR", "23503"));
    }

    [Fact]
    public async Task RemoveRoleAsync_ExistingRole_RemovesRole()
    {
        // Arrange
        await _service.AssignRoleAsync(_testEdjeId, "EDJEr", "admin@test.com", "Assignment");

        // Act
        await _service.RemoveRoleAsync(_testEdjeId, "EDJEr", "admin@test.com", "No longer needed");

        // Assert
        var roles = await _service.GetRolesAsync(_testEdjeId);
        roles.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveRoleAsync_NonExistentRole_IsNoOp()
    {
        // Act & Assert - should not throw
        await _service.RemoveRoleAsync(_testEdjeId, "NonExistent", "admin@test.com", "Cleanup");
    }

    [Fact]
    public async Task RemoveRoleAsync_SuperAdminRemovingOwnSuperAdmin_ThrowsInvalidOperation()
    {
        // Arrange
        await _service.AssignRoleAsync(_testEdjeId, "SuperAdmin", "admin@test.com", "Assignment");

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(
            () => _service.RemoveRoleAsync(_testEdjeId, "SuperAdmin", _testEdjeId.ToString(), "Self removal"));
    }

    [Fact]
    public async Task RemoveRoleAsync_OtherSuperAdminRemovingSuperAdmin_Succeeds()
    {
        // Arrange
        await _service.AssignRoleAsync(_testEdjeId, "SuperAdmin", "admin@test.com", "Assignment");

        // Act
        await _service.RemoveRoleAsync(_testEdjeId, "SuperAdmin", _otherEdjeId.ToString(), "Admin action");

        // Assert
        var roles = await _service.GetRolesAsync(_testEdjeId);
        roles.ShouldNotContain("SuperAdmin");
    }

    [Fact]
    public async Task GetUsersByRoleAsync_ReturnsAllUsersWithRole()
    {
        // Arrange
        await _repository.AddAsync(new UserRole { EdjeId = _testEdjeId, Role = "EDJEr" });
        await _repository.AddAsync(new UserRole { EdjeId = _otherEdjeId, Role = "EDJEr" });
        await _repository.AddAsync(new UserRole { EdjeId = _testEdjeId, Role = "Manager" });

        // Act
        var result = await _service.GetUsersByRoleAsync("EDJEr");

        // Assert
        result.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SyncFromProfileAsync_AddsMissingRoles()
    {
        // Arrange
        await _repository.AddAsync(new UserRole { EdjeId = _testEdjeId, Role = "EDJEr" });

        // Act
        await _service.SyncFromProfileAsync(_testEdjeId, ["EDJEr", "Manager", "Admin"]);

        // Assert
        var roles = await _service.GetRolesAsync(_testEdjeId);
        roles.Count.ShouldBe(3);
        roles.ShouldContain("EDJEr");
        roles.ShouldContain("Manager");
        roles.ShouldContain("Admin");
    }

    [Fact]
    public async Task HasAnyRoleAsync_EmptyTable_ReturnsFalse()
    {
        // Arrange -- no roles seeded (empty user_roles table)
        // This is Scenario B prep: when user_roles table has no entries
        // for a user, HasAnyRoleAsync returns false. The IAuthorizationResolver
        // handles the JWT check separately, so this verifies the DB-only path.

        // Act
        var result = await _service.HasAnyRoleAsync(_testEdjeId, "SuperAdmin", "EDJEr", "Manager");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task GetRolesAsync_EmptyTable_ReturnsEmptyList()
    {
        // Arrange -- no roles seeded

        // Act
        var result = await _service.GetRolesAsync(_testEdjeId);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task SyncFromProfileAsync_GivenPrivilegesContainingACompassRole_NeverPersistsTheCompassRole()
    {
        // Arrange -- a Compass role arriving here exactly matches what SignInService.BuildRoles
        // produces after a sign-in where the user holds a Compass Google group. BR-12/FR-013:
        // Compass authority is derived per session and must never be persisted -- and
        // CompositeAuthorizationResolver's DB fallback means a persisted row here is not inert
        // data, it is a live grant independent of the deriving session.

        // Act
        await _service.SyncFromProfileAsync(_testEdjeId, ["EDJEr", RolePolicy.CompassOpsRole]);

        // Assert -- the timesheet role persists as usual; the Compass role must never reach the table.
        var roles = await _service.GetRolesAsync(_testEdjeId);
        roles.ShouldContain("EDJEr");
        roles.ShouldNotContain(RolePolicy.CompassOpsRole);
    }

    [Fact]
    public async Task SyncFromProfileAsync_RemovesExtraRoles()
    {
        // Arrange
        await _repository.AddAsync(new UserRole { EdjeId = _testEdjeId, Role = "EDJEr" });
        await _repository.AddAsync(new UserRole { EdjeId = _testEdjeId, Role = "Manager" });
        await _repository.AddAsync(new UserRole { EdjeId = _testEdjeId, Role = "Admin" });

        // Act
        await _service.SyncFromProfileAsync(_testEdjeId, ["EDJEr"]);

        // Assert
        var roles = await _service.GetRolesAsync(_testEdjeId);
        roles.Count.ShouldBe(1);
        roles.ShouldContain("EDJEr");
    }

    [Fact]
    public async Task EnsureBaseRoleForAllEmployeesAsync_AddsEdjerForNewEmployees()
    {
        // Arrange
        var employees = new List<(Guid EdjeId, string Name)>
        {
            (_testEdjeId, "Alice"),
            (_otherEdjeId, "Bob")
        };

        // Act
        var added = await _service.EnsureBaseRoleForAllEmployeesAsync(employees);

        // Assert
        added.ShouldBe(2);
        (await _service.HasRoleAsync(_testEdjeId, "EDJEr")).ShouldBeTrue();
        (await _service.HasRoleAsync(_otherEdjeId, "EDJEr")).ShouldBeTrue();
    }

    [Fact]
    public async Task EnsureBaseRoleForAllEmployeesAsync_SkipsExistingEdjer()
    {
        // Arrange
        await _repository.AddAsync(new UserRole { EdjeId = _testEdjeId, Role = "EDJEr", CreatedBy = "test", UpdatedBy = "test" });
        var employees = new List<(Guid EdjeId, string Name)>
        {
            (_testEdjeId, "Alice"),
            (_otherEdjeId, "Bob")
        };

        // Act
        var added = await _service.EnsureBaseRoleForAllEmployeesAsync(employees);

        // Assert
        added.ShouldBe(1); // Only Bob added
        (await _service.HasRoleAsync(_testEdjeId, "EDJEr")).ShouldBeTrue();
        (await _service.HasRoleAsync(_otherEdjeId, "EDJEr")).ShouldBeTrue();
    }

    [Fact]
    public async Task EnsureBaseRoleForAllEmployeesAsync_EmptyList_ReturnsZero()
    {
        // Act
        var added = await _service.EnsureBaseRoleForAllEmployeesAsync([]);

        // Assert
        added.ShouldBe(0);
    }

    [Fact]
    public async Task SyncFromProfileAsync_RemoveRaceFromConcurrentRequest_DoesNotThrow()
    {
        // Mirror to the duplicate-INSERT race: two concurrent sync calls both
        // remove the same UserRole; the second SaveChanges sees ROW_COUNT()=0
        // and Oracle's provider throws DbUpdateConcurrencyException. The
        // desired state is already in place, so we must swallow it.
        var repository = new InMemoryUserRoleRepository();
        await repository.AddAsync(new UserRole { EdjeId = _testEdjeId, Role = "Manager" });

        // Wire a context that throws DbUpdateConcurrencyException on
        // SaveChangesAsync to simulate the concurrent-delete race.
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using var context = new ConcurrencyThrowingDbContext(options);
        var service = new UserRoleService(repository, new StubAuditService(), context);

        // Act + Assert: must not throw
        await service.SyncFromProfileAsync(_testEdjeId, ["EDJEr"]);
    }

    [Fact]
    public async Task SyncFromProfileAsync_DuplicateInsertFromConcurrentRequest_DoesNotThrow()
    {
        // Reproduces the production race observed during parallel webkit E2E
        // runs: two concurrent requests both observe an empty user_roles table
        // for an EdjeId, both INSERT the same (EdjeId, Role), and the second
        // SaveChanges fails with a Postgres unique_violation (SQLSTATE 23505 on
        // ix_user_roles_edje_id_role, surfaced as a PostgresException wrapped in
        // a DbUpdateException).
        // SyncFromProfileAsync MUST treat this as a benign "another request
        // already added it" and complete without bubbling the error to the
        // request pipeline (which currently 500s the timesheet GET).

        var repository = new ThrowOnAddUserRoleRepository();
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using var context = new LeapDbContext(options);
        var service = new UserRoleService(repository, new StubAuditService(), context);

        // Act + Assert: must not throw
        await service.SyncFromProfileAsync(_testEdjeId, ["Manager"]);
    }

    /// <summary>
    /// DbContext stub that raises <see cref="DbUpdateConcurrencyException"/>
    /// on every SaveChangesAsync call, simulating the Oracle MySQL provider's
    /// "0 rows affected -> ROW_COUNT mismatch" path observed when two requests
    /// race to remove the same user_roles row.
    /// </summary>
    private class ConcurrencyThrowingDbContext(DbContextOptions<LeapDbContext> options)
        : LeapDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateConcurrencyException(
                "The database operation was expected to affect 1 row(s), but actually affected 0 row(s)");
    }

    [Fact]
    public async Task SyncFromProfileAsync_ConcurrentInsertDuringSync_ReReadsBeforeDelete()
    {
        // Stronger invariant for the race-safe implementation: when the
        // baseline read MISSES a row that another caller inserted before
        // our delete pass, our delete pass must NOT consider that row "extra"
        // and remove it — even though the row is not in our desired set.
        //
        // This is what the new InMemory branch of SyncFromProfileAsync
        // ensures: the second GetByEdjeIdAsync re-read (after the insert
        // pass) drives the delete-extras calculation. Old impl's single
        // up-front stale read misses the concurrent row entirely, so it
        // computed extras=[] and a row that should still be removed gets
        // left in place — that's the OPPOSITE failure mode but stems from
        // the same single-read flaw.
        //
        // Concretely: stale baseline says "{EDJEr}", live state contains
        // "{EDJEr, Admin}" because Admin was inserted by a concurrent
        // caller. Our Sync(desired={EDJEr}) should NOT add anything (EDJEr
        // already present in stale view) and should evaluate "extras" against
        // the FRESH view to decide whether Admin should be removed.
        //
        // The race-safe contract: "after Sync(desired={X}) returns for
        // EdjeId E, the rows for E in user_roles are EXACTLY {X}". With
        // single-stale-read implementation, Admin survives. With re-read
        // implementation, Admin is removed.
        var repository = new StaleReadUserRoleRepository(initial: ["EDJEr"]);
        // Inject a row directly into the live state (simulating concurrent
        // insert by another caller) without touching the stale snapshot.
        repository.InjectLiveOnly(_testEdjeId, "Admin");

        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using var context = new LeapDbContext(options);
        var service = new UserRoleService(repository, new StubAuditService(), context);

        // Act: Sync only wants EDJEr. The stale baseline already contains
        // EDJEr, so the sync's "missing" add list is empty. The "extras"
        // calculation is what differs: stale view says no extras (no Admin
        // in stale view), fresh view says Admin is extra and must be removed.
        await service.SyncFromProfileAsync(_testEdjeId, ["EDJEr"]);

        // Assert: race-safe impl removes the concurrently-inserted Admin.
        var finalRoles = repository.SnapshotAll()
            .Where(r => r.EdjeId == _testEdjeId)
            .Select(r => r.Role)
            .ToHashSet();
        finalRoles.ShouldContain("EDJEr");
        finalRoles.ShouldNotContain("Admin");
    }

    /// <summary>
    /// Test double that simulates the read-then-write race window: every
    /// <see cref="GetByEdjeIdAsync"/> call returns the SAME baseline snapshot
    /// captured at construction time, regardless of subsequent writes. Adds
    /// and removes still mutate a single shared list so the final state
    /// reflects the union of all writes that survived the race.
    /// </summary>
    private class StaleReadUserRoleRepository : IUserRoleRepository
    {
        private readonly List<UserRole> _state;
        private readonly List<UserRole> _staleSnapshot;
        private int _nextId = 1;
        private int _readCount;

        public StaleReadUserRoleRepository(IReadOnlyList<string>? initial = null)
        {
            _state = [];
            _staleSnapshot = [];
            if (initial is null)
            {
                return;
            }

            var seedEdjeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            foreach (var role in initial)
            {
                var ur = new UserRole { Id = _nextId++, EdjeId = seedEdjeId, Role = role };
                _state.Add(ur);
                _staleSnapshot.Add(ur);
            }
        }

        public Task<IReadOnlyList<UserRole>> GetByEdjeIdAsync(Guid edjeId)
        {
            // Models the read-then-write race window: the FIRST read returns
            // the stale baseline that both racing syncs observed when they
            // entered the window. Subsequent reads return the fresh state —
            // that re-read is the contract the race-safe implementation
            // establishes (insert first, then re-read, then delete only
            // confirmed-extras). The old single-read implementation never
            // takes the second read and so cannot see the live state.
            _readCount++;
            var source = _readCount == 1 ? _staleSnapshot : _state;
            var snapshot = source.Where(r => r.EdjeId == edjeId).ToList().AsReadOnly();
            return Task.FromResult<IReadOnlyList<UserRole>>(snapshot);
        }

        public Task<IReadOnlyList<UserRole>> GetByRoleAsync(string role) =>
            Task.FromResult<IReadOnlyList<UserRole>>(_state.Where(r => r.Role == role).ToList().AsReadOnly());

        public Task<UserRole?> FindAsync(Guid edjeId, string role) =>
            Task.FromResult(_state.FirstOrDefault(r => r.EdjeId == edjeId && r.Role == role));

        public Task<UserRole> AddAsync(UserRole userRole)
        {
            // Reject duplicates the way Postgres would: violates ix_user_roles_edje_id_role
            // (SQLSTATE 23505 unique_violation, wrapped by EF Core in a DbUpdateException).
            if (_state.Any(r => r.EdjeId == userRole.EdjeId && r.Role == userRole.Role))
            {
                throw new DbUpdateException(
                    "An error occurred while saving the entity changes.",
                    new PostgresException(
                        "duplicate key value violates unique constraint \"ix_user_roles_edje_id_role\"",
                        "ERROR", "ERROR", "23505"));
            }

            userRole.Id = _nextId++;
            _state.Add(userRole);
            return Task.FromResult(userRole);
        }

        public Task RemoveAsync(UserRole userRole)
        {
            _state.RemoveAll(r => r.EdjeId == userRole.EdjeId && r.Role == userRole.Role);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UserRole>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<UserRole>>(_state.ToList().AsReadOnly());

        public IReadOnlyList<UserRole> SnapshotAll() => _state.ToList().AsReadOnly();

        /// <summary>
        /// Injects a row into the live backing state WITHOUT touching the
        /// stale snapshot. Models a concurrent caller's INSERT landing
        /// between this Sync's first (stale) read and any subsequent reads.
        /// </summary>
        public void InjectLiveOnly(Guid edjeId, string role) =>
            _state.Add(new UserRole { Id = _nextId++, EdjeId = edjeId, Role = role });
    }

    [Fact]
    public async Task SyncFromProfileAsync_NonDuplicateDbUpdateException_BubblesUp()
    {
        // Negative-case for IsDuplicateKeyViolation: when AddAsync throws a
        // DbUpdateException that does NOT carry "Duplicate entry" anywhere in
        // its inner-exception chain, SyncFromProfileAsync must let the exception
        // surface so a real DB problem (FK violation, deadlock, schema drift)
        // doesn't get silently swallowed alongside the benign duplicate-key race.
        var repository = new ThrowGenericDbUpdateExceptionRepository();
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using var context = new LeapDbContext(options);
        var service = new UserRoleService(repository, new StubAuditService(), context);

        var ex = await Should.ThrowAsync<DbUpdateException>(
            () => service.SyncFromProfileAsync(_testEdjeId, ["Manager"]));
        ex.Message.ShouldNotContain("Duplicate entry");
    }

    /// <summary>
    /// Test double that simulates a unique-key violation on AddAsync, mirroring
    /// the production Postgres behavior (SQLSTATE 23505 unique_violation) when
    /// two concurrent requests both INSERT the same (EdjeId, Role) row.
    /// </summary>
    private class ThrowOnAddUserRoleRepository : IUserRoleRepository
    {
        public Task<IReadOnlyList<UserRole>> GetByEdjeIdAsync(Guid edjeId) =>
            Task.FromResult<IReadOnlyList<UserRole>>([]);

        public Task<IReadOnlyList<UserRole>> GetByRoleAsync(string role) =>
            Task.FromResult<IReadOnlyList<UserRole>>([]);

        public Task<UserRole?> FindAsync(Guid edjeId, string role) =>
            Task.FromResult<UserRole?>(null);

        public Task<UserRole> AddAsync(UserRole userRole) =>
            throw new DbUpdateException(
                "An error occurred while saving the entity changes.",
                new PostgresException(
                    $"duplicate key value violates unique constraint \"ix_user_roles_edje_id_role\"",
                    "ERROR", "ERROR", "23505"));

        public Task RemoveAsync(UserRole userRole) => Task.CompletedTask;

        public Task<IReadOnlyList<UserRole>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<UserRole>>([]);
    }

    /// <summary>
    /// Test double that simulates a non-duplicate-key DbUpdateException
    /// (e.g. an FK violation or a deadlock) so we can verify
    /// IsDuplicateKeyViolation correctly returns false and the exception
    /// bubbles up instead of being silently swallowed.
    /// </summary>
    private class ThrowGenericDbUpdateExceptionRepository : IUserRoleRepository
    {
        public Task<IReadOnlyList<UserRole>> GetByEdjeIdAsync(Guid edjeId) =>
            Task.FromResult<IReadOnlyList<UserRole>>([]);

        public Task<IReadOnlyList<UserRole>> GetByRoleAsync(string role) =>
            Task.FromResult<IReadOnlyList<UserRole>>([]);

        public Task<UserRole?> FindAsync(Guid edjeId, string role) =>
            Task.FromResult<UserRole?>(null);

        public Task<UserRole> AddAsync(UserRole userRole) =>
            throw new DbUpdateException(
                "Foreign key constraint fails on `user_roles.fk_user_roles_edje_id`",
                new InvalidOperationException("inner cause"));

        public Task RemoveAsync(UserRole userRole) => Task.CompletedTask;

        public Task<IReadOnlyList<UserRole>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<UserRole>>([]);
    }

    /// <summary>Stub audit service for unit testing.</summary>
    private class StubAuditService : IAuditService
    {
        public Task LogAsync(AuditEntry entry) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditLogResponse>> GetByEntityAsync(string entityType, string entityId) =>
            Task.FromResult<IReadOnlyList<AuditLogResponse>>([]);
        public Task<PaginatedAuditLogResponse> BrowseAsync(string? entityType, string? actor, string? employeeId, DateTime? fromDate, DateTime? toDate, int page, int pageSize) =>
            Task.FromResult(new PaginatedAuditLogResponse([], 0, page, pageSize));
        public Task<IReadOnlyList<string>> GetDistinctEntityTypesAsync() =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
