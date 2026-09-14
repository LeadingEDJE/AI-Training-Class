using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Data.SeedData;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

/// <summary>
/// Pins the config-driven bootstrap SuperAdmin seeder (quick task 260729-sqc, D-01..D-04). Runs the
/// REAL <see cref="PersonProvisioningService"/> against an InMemory context with a recording
/// <see cref="IUserRoleService"/>, mirroring <c>AbsorbedDirectorySeederTests</c>. The load-bearing
/// properties: unset config is a complete no-op (so local dev and CI are byte-identical to today),
/// re-running is safe on every pod restart, and an explicitly deactivated person is never re-admitted
/// just because configuration names them.
/// </summary>
public class BootstrapSuperAdminSeederTests
{
    private const string Wren = "wren.castellan@leadingedje.com";
    private const string AveryEmail = "avery.quinn@example.com";

    private static LeapDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    private static BootstrapSuperAdminSeeder CreateSeeder(
        LeapDbContext context,
        RecordingUserRoleService roles,
        BootstrapAdminOptions options,
        ILogger<BootstrapSuperAdminSeeder>? logger = null) =>
        new(
            new PersonProvisioningService(context, NullLogger<PersonProvisioningService>.Instance),
            roles,
            Options.Create(options),
            logger ?? NullLogger<BootstrapSuperAdminSeeder>.Instance);

    [Fact]
    public async Task SeedAsync_OptionsUnset_WritesNothing()
    {
        // Arrange — D-04: a default-constructed options object is what local dev and CI see.
        using var context = CreateContext();
        var roles = new RecordingUserRoleService();
        var seeder = CreateSeeder(context, roles, new BootstrapAdminOptions());
        var ct = TestContext.Current.CancellationToken;

        // Act
        await seeder.SeedAsync();

        // Assert
        (await context.People.CountAsync(ct)).ShouldBe(0);
        roles.Assignments.ShouldBeEmpty();
    }

    [Fact]
    public async Task SeedAsync_EmptyList_WritesNothing()
    {
        // Arrange
        using var context = CreateContext();
        var roles = new RecordingUserRoleService();
        var seeder = CreateSeeder(context, roles, new BootstrapAdminOptions { SuperAdmins = [] });
        var ct = TestContext.Current.CancellationToken;

        // Act
        await seeder.SeedAsync();

        // Assert
        (await context.People.CountAsync(ct)).ShouldBe(0);
        roles.Assignments.ShouldBeEmpty();
    }

    [Fact]
    public async Task SeedAsync_TwoConfiguredEmails_CreatesActivePeopleWithBootstrapSourceAndSuperAdminGrants()
    {
        // Arrange
        using var context = CreateContext();
        var roles = new RecordingUserRoleService();
        var seeder = CreateSeeder(context, roles, new BootstrapAdminOptions { SuperAdmins = [Wren, AveryEmail] });
        var ct = TestContext.Current.CancellationToken;

        // Act
        await seeder.SeedAsync();

        // Assert — one active, bootstrap-provenance person per configured email with a minted EdjeId.
        var people = await context.People.ToListAsync(ct);
        people.Count.ShouldBe(2);
        people.Select(p => p.Email).ShouldBe(new[] { Wren, AveryEmail }, ignoreOrder: true);
        people.ShouldAllBe(p => p.IsActive);
        people.ShouldAllBe(p => p.Source == PersonSource.BootstrapSeed);
        people.ShouldAllBe(p => p.EdjeId != null && p.EdjeId != Guid.Empty);

        // Assert — D-02: the SuperAdmin grant is written directly, so bootstrap works before the
        // Google group is attached. The actor identifies the bootstrap so the audit entry explains it.
        roles.Assignments.Count.ShouldBe(2);
        roles.Assignments.ShouldAllBe(a => a.Role == RolePolicy.SuperAdmin);
        roles.Assignments.ShouldAllBe(a => a.Actor.Contains("bootstrap"));
        roles.Assignments.ShouldAllBe(a => a.Reason.Length > 0);
        roles.Assignments.Select(a => a.EdjeId).ShouldBe(
            people.Select(p => p.EdjeId!.Value), ignoreOrder: true);
    }

    [Fact]
    public async Task SeedAsync_ConfiguredEmails_DerivesAPlaceholderNameSoThePeopleListIsNotBlank()
    {
        // Arrange — P2 (2026-07-30): the seeder used to pass `displayName: null`, so Admin -> People
        // listed all four bootstrap admins with a blank NAME until they had each signed in.
        using var context = CreateContext();
        var roles = new RecordingUserRoleService();
        var seeder = CreateSeeder(context, roles, new BootstrapAdminOptions { SuperAdmins = [Wren, AveryEmail] });
        var ct = TestContext.Current.CancellationToken;

        // Act
        await seeder.SeedAsync();

        // Assert — derived from the `first.last@` local part, title-cased.
        var people = await context.People.ToListAsync(ct);
        var ben = people.Single(p => p.Email == AveryEmail);
        ben.FirstName.ShouldBe("Avery");
        ben.LastName.ShouldBe("Quinn");
        var wren = people.Single(p => p.Email == Wren);
        wren.FirstName.ShouldBe("Wren");
        wren.LastName.ShouldBe("Castellan");
    }

    [Fact]
    public async Task SeedAsync_EmailIsNotDerivable_LeavesTheNameNullRatherThanGuessing()
    {
        // Arrange — a wrong name in a people list is worse than a blank one, so an ambiguous local part
        // stays identity-only (D-08). The assertion name fills it in on first sign-in.
        using var context = CreateContext();
        var roles = new RecordingUserRoleService();
        var seeder = CreateSeeder(
            context, roles, new BootstrapAdminOptions { SuperAdmins = ["jkirk@leadingedje.com"] });
        var ct = TestContext.Current.CancellationToken;

        // Act
        await seeder.SeedAsync();

        // Assert
        var person = await context.People.SingleAsync(ct);
        person.FirstName.ShouldBeNull();
        person.LastName.ShouldBeNull();
    }

    [Fact]
    public async Task SeedAsync_ExistingPersonWithNoName_BackfillsTheDerivedName()
    {
        // Arrange — the case observed on deployed dev 2026-07-30: the four bootstrap admins already had
        // rows from an EARLIER pod start (created when the seeder passed displayName: null), so deriving
        // a name only on INSERT never reached them. Admin -> People still listed them blank, which is
        // exactly the "admins who haven't signed in yet" case this was supposed to fix.
        using var context = CreateContext();
        var id = Guid.CreateVersion7();
        context.People.Add(new Person
        {
            Id = id,
            EdjeId = id,
            Email = AveryEmail,
            FirstName = null,
            LastName = null,
            IsActive = true,
            Source = PersonSource.BootstrapSeed,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var roles = new RecordingUserRoleService();
        var seeder = CreateSeeder(context, roles, new BootstrapAdminOptions { SuperAdmins = [AveryEmail] });
        var ct = TestContext.Current.CancellationToken;

        // Act
        await seeder.SeedAsync();

        // Assert — no duplicate row, and the existing one now carries the derived placeholder.
        var person = await context.People.SingleAsync(ct);
        person.EdjeId.ShouldBe(id);
        person.FirstName.ShouldBe("Avery");
        person.LastName.ShouldBe("Quinn");
    }

    [Fact]
    public async Task SeedAsync_ExistingPersonWithNoNameAndUnderivableEmail_StaysBlank()
    {
        // Arrange — backfill must not invent a name where the local part carries none (D-08).
        using var context = CreateContext();
        var id = Guid.CreateVersion7();
        context.People.Add(new Person
        {
            Id = id,
            EdjeId = id,
            Email = "jkirk@leadingedje.com",
            IsActive = true,
            Source = PersonSource.BootstrapSeed,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var roles = new RecordingUserRoleService();
        var seeder = CreateSeeder(
            context, roles, new BootstrapAdminOptions { SuperAdmins = ["jkirk@leadingedje.com"] });
        var ct = TestContext.Current.CancellationToken;

        // Act
        await seeder.SeedAsync();

        // Assert
        var person = await context.People.SingleAsync(ct);
        person.FirstName.ShouldBeNull();
        person.LastName.ShouldBeNull();
    }

    [Fact]
    public async Task SeedAsync_ExistingPersonWithAName_DoesNotOverwriteItWithADerivedGuess()
    {
        // Arrange — the seeder runs on EVERY pod start. Once a real name is stored (from the Google
        // assertion, or from HR), a later boot must not clobber it with an email-derived placeholder.
        using var context = CreateContext();
        var id = Guid.CreateVersion7();
        context.People.Add(new Person
        {
            Id = id,
            EdjeId = id,
            Email = AveryEmail,
            FirstName = "Benjamin",
            LastName = "Alvarez-Diaz",
            IsActive = true,
            Source = PersonSource.BootstrapSeed,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var roles = new RecordingUserRoleService();
        var seeder = CreateSeeder(context, roles, new BootstrapAdminOptions { SuperAdmins = [AveryEmail] });
        var ct = TestContext.Current.CancellationToken;

        // Act
        await seeder.SeedAsync();

        // Assert
        var person = await context.People.SingleAsync(ct);
        person.FirstName.ShouldBe("Benjamin");
        person.LastName.ShouldBe("Alvarez-Diaz");
    }

    [Fact]
    public async Task SeedAsync_CalledTwice_IsIdempotentAcrossPodRestarts()
    {
        // Arrange — D-03: the seeder runs on EVERY pod start.
        using var context = CreateContext();
        var roles = new RecordingUserRoleService();
        var seeder = CreateSeeder(context, roles, new BootstrapAdminOptions { SuperAdmins = [Wren, AveryEmail] });
        var ct = TestContext.Current.CancellationToken;

        // Act
        await seeder.SeedAsync();
        await seeder.SeedAsync();

        // Assert
        (await context.People.CountAsync(ct)).ShouldBe(2);
        roles.Assignments.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SeedAsync_ConfiguredEmailAlreadyActivePersonDifferingByCase_ReusesRowAndPreservesProvenance()
    {
        // Arrange — an HR-imported row (Source NULL) whose email differs only in case.
        using var context = CreateContext();
        var edjeId = Guid.NewGuid();
        context.People.Add(new Person
        {
            Id = Guid.NewGuid(),
            EdjeId = edjeId,
            Email = "Wren.Castellan@LeadingEDJE.com",
            FirstName = "Wren",
            LastName = "Castellan",
            IsActive = true,
            Source = null,
        });
        var ct = TestContext.Current.CancellationToken;
        await context.SaveChangesAsync(ct);
        var roles = new RecordingUserRoleService();
        var seeder = CreateSeeder(context, roles, new BootstrapAdminOptions { SuperAdmins = [Wren] });

        // Act
        await seeder.SeedAsync();

        // Assert — no duplicate row, the existing EdjeId is granted, and HR provenance stays NULL.
        (await context.People.CountAsync(ct)).ShouldBe(1);
        (await context.People.SingleAsync(ct)).Source.ShouldBeNull();
        roles.Assignments.Count.ShouldBe(1);
        roles.Assignments[0].EdjeId.ShouldBe(edjeId);
        roles.Assignments[0].Role.ShouldBe(RolePolicy.SuperAdmin);
    }

    [Fact]
    public async Task SeedAsync_ConfiguredEmailIsDeactivatedPerson_SkipsWithWarningAndStillSeedsTheRest()
    {
        // Arrange — D-06 consistency: configuration must not re-admit a deliberately deactivated person,
        // and one such entry must not abort the bootstrap for everyone else.
        using var context = CreateContext();
        context.People.Add(new Person
        {
            Id = Guid.NewGuid(),
            EdjeId = Guid.NewGuid(),
            Email = Wren,
            IsActive = false,
        });
        var ct = TestContext.Current.CancellationToken;
        await context.SaveChangesAsync(ct);
        var roles = new RecordingUserRoleService();
        var logger = new CapturingLogger<BootstrapSuperAdminSeeder>();
        var seeder = CreateSeeder(context, roles, new BootstrapAdminOptions { SuperAdmins = [Wren, AveryEmail] }, logger);

        // Act
        await seeder.SeedAsync();

        // Assert — no reactivation, no grant for Wren; Avery still provisioned.
        var wren = await context.People.SingleAsync(p => p.Email == Wren, ct);
        wren.IsActive.ShouldBeFalse();
        (await context.People.CountAsync(ct)).ShouldBe(2);

        roles.Assignments.Count.ShouldBe(1);
        var ben = await context.People.SingleAsync(p => p.Email == AveryEmail, ct);
        roles.Assignments[0].EdjeId.ShouldBe(ben.EdjeId!.Value);

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SeedAsync_BlankAndCaseDuplicateEntries_CollapseToOnePersonAndOneGrant()
    {
        // Arrange
        using var context = CreateContext();
        var roles = new RecordingUserRoleService();
        var seeder = CreateSeeder(context, roles, new BootstrapAdminOptions
        {
            SuperAdmins = ["   ", Wren, "WREN.CASTELLAN@LEADINGEDJE.COM", "  wren.castellan@leadingedje.com  ", ""],
        });
        var ct = TestContext.Current.CancellationToken;

        // Act
        await seeder.SeedAsync();

        // Assert
        (await context.People.CountAsync(ct)).ShouldBe(1);
        roles.Assignments.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SeedAsync_ProvisioningThrowsForOneEntry_LogsErrorAndStillSeedsTheRest()
    {
        // Arrange — T-sqc-07: one malformed/conflicting entry must not deny access to every other
        // bootstrap admin, and it must not abort startup seeding.
        using var context = CreateContext();
        var roles = new RecordingUserRoleService();
        var logger = new CapturingLogger<BootstrapSuperAdminSeeder>();
        var provisioning = new ScriptedPersonProvisioningService(email =>
            string.Equals(email, Wren, StringComparison.OrdinalIgnoreCase)
                ? throw new InvalidOperationException("provisioning blew up")
                : PersonProvisionResult.ForCreated(Guid.NewGuid(), email!, email!));
        var seeder = new BootstrapSuperAdminSeeder(
            provisioning, roles, Options.Create(new BootstrapAdminOptions { SuperAdmins = [Wren, AveryEmail] }), logger);

        // Act
        await seeder.SeedAsync();

        // Assert — Avery still got their grant; the failure was logged, not thrown.
        roles.Assignments.Count.ShouldBe(1);
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task SeedAsync_ProvisioningReportsRejected_SkipsWithWarningAndGrantsNothing()
    {
        // Arrange — defensive: an entry the provisioning core cannot use at all.
        using var context = CreateContext();
        var roles = new RecordingUserRoleService();
        var logger = new CapturingLogger<BootstrapSuperAdminSeeder>();
        var provisioning = new ScriptedPersonProvisioningService(_ => PersonProvisionResult.ForRejected());
        var seeder = new BootstrapSuperAdminSeeder(
            provisioning, roles, Options.Create(new BootstrapAdminOptions { SuperAdmins = [Wren] }), logger);

        // Act
        await seeder.SeedAsync();

        // Assert
        roles.Assignments.ShouldBeEmpty();
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
    }

    /// <summary>Provisioning double driven by a per-email script (or throw).</summary>
    private sealed class ScriptedPersonProvisioningService(
        Func<string?, PersonProvisionResult> respond) : IPersonProvisioningService
    {
        /// <summary>Every (email, displayName) pair the seeder passed — pins the P2 email-derived name.</summary>
        public List<(string? Email, string? DisplayName)> Calls { get; } = [];

        public Task<PersonProvisionResult> EnsurePersonAsync(string? email, string? displayName, string source)
        {
            Calls.Add((email, displayName));
            return Task.FromResult(respond(email));
        }

        public Task<string> EnsureDisplayNameAsync(
            Guid edjeId, string? assertionName, string resolvedDisplayName) =>
            Task.FromResult(resolvedDisplayName);

        /// <summary>Records backfill attempts so the seeder's derived-name call can be asserted.</summary>
        public List<(Guid EdjeId, string? CandidateName)> BackfillCalls { get; } = [];

        public Task TryFillMissingDisplayNameAsync(Guid edjeId, string? candidateName)
        {
            BackfillCalls.Add((edjeId, candidateName));
            return Task.CompletedTask;
        }
    }

    /// <summary>Records <see cref="AssignRoleAsync"/> calls idempotently (matching the real service).</summary>
    private sealed class RecordingUserRoleService : IUserRoleService
    {
        public List<(Guid EdjeId, string Role, string Actor, string Reason)> Assignments { get; } = [];

        public Task AssignRoleAsync(Guid edjeId, string role, string actor, string reason)
        {
            if (!Assignments.Any(a => a.EdjeId == edjeId && a.Role == role))
            {
                Assignments.Add((edjeId, role, actor, reason));
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetRolesAsync(Guid edjeId) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<bool> HasRoleAsync(Guid edjeId, string role) => Task.FromResult(false);
        public Task<bool> HasAnyRoleAsync(Guid edjeId, params string[] roles) => Task.FromResult(false);
        public Task RemoveRoleAsync(Guid edjeId, string role, string actor, string reason) => Task.CompletedTask;
        public Task<IReadOnlyList<UserRole>> GetUsersByRoleAsync(string role) =>
            Task.FromResult<IReadOnlyList<UserRole>>([]);
        public Task SyncFromProfileAsync(Guid edjeId, IEnumerable<string> privileges, string actor = "dev-sync") =>
            Task.CompletedTask;
        public Task<int> EnsureBaseRoleForAllEmployeesAsync(IEnumerable<(Guid EdjeId, string Name)> employees) =>
            Task.FromResult(0);
    }

    /// <summary>Minimal logger capturing level + rendered message so log-level assertions are possible.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
