using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Pins the shared find-or-create core that both the bootstrap SuperAdmin seeder and the SAML
/// sign-in auto-create branch depend on (quick task 260729-sqc). The correctness rules under test:
/// exactly one row per email (case-insensitively, matching <c>ix_people_email_lower</c>), an
/// explicitly deactivated person is NEVER reactivated and NEVER duplicated (D-06) — including the
/// null-<c>EdjeId</c> variant that <c>EmployeeDirectoryService.GetEmployeeByEmailAsync</c> collapses
/// to <c>null</c> — and every row this app mints carries a provenance <c>Source</c> (D-07).
/// </summary>
public class PersonProvisioningServiceTests
{
    private const string Source = PersonSource.SamlAutoCreate;

    private static LeapDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    private static PersonProvisioningService CreateService(LeapDbContext context) =>
        new(context, NullLogger<PersonProvisioningService>.Instance);

    [Fact]
    public async Task EnsurePersonAsync_NoExistingRow_CreatesActivePersonWithMintedEdjeIdAndSource()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await service.EnsurePersonAsync("ada@leadingedje.com", "Ada Lovelace", Source);

        // Assert
        result.Outcome.ShouldBe(PersonProvisionOutcome.Created);
        result.EdjeId.ShouldNotBe(Guid.Empty);
        result.Email.ShouldBe("ada@leadingedje.com");

        var person = await context.People.SingleAsync(ct);
        person.Id.ShouldNotBe(Guid.Empty);
        person.EdjeId.ShouldBe(result.EdjeId);
        person.IsActive.ShouldBeTrue();
        person.Email.ShouldBe("ada@leadingedje.com");
        person.Source.ShouldBe(Source);
    }

    [Fact]
    public async Task EnsurePersonAsync_TwoTokenDisplayName_SplitsIntoFirstAndLastName()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await service.EnsurePersonAsync("ada@leadingedje.com", "Ada Lovelace", Source);

        // Assert
        var person = await context.People.SingleAsync(ct);
        person.FirstName.ShouldBe("Ada");
        person.LastName.ShouldBe("Lovelace");
        result.DisplayName.ShouldBe("Ada Lovelace");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EnsurePersonAsync_BlankDisplayName_LeavesNamesNullAndFallsBackToEmail(string? displayName)
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await service.EnsurePersonAsync("ada@leadingedje.com", displayName, Source);

        // Assert — no name to store, but the caller must still be able to stamp a non-empty claim.
        var person = await context.People.SingleAsync(ct);
        person.FirstName.ShouldBeNull();
        person.LastName.ShouldBeNull();
        result.DisplayName.ShouldBe("ada@leadingedje.com");
    }

    [Fact]
    public async Task EnsurePersonAsync_SingleTokenDisplayName_StoresFirstNameOnly()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;

        // Act
        await service.EnsurePersonAsync("ada@leadingedje.com", "Ada", Source);

        // Assert
        var person = await context.People.SingleAsync(ct);
        person.FirstName.ShouldBe("Ada");
        person.LastName.ShouldBeNull();
    }

    [Fact]
    public async Task EnsurePersonAsync_ThreeTokenDisplayName_KeepsRemainderAsLastName()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;

        // Act
        await service.EnsurePersonAsync("ada@leadingedje.com", "Ada B. Lovelace", Source);

        // Assert
        var person = await context.People.SingleAsync(ct);
        person.FirstName.ShouldBe("Ada");
        person.LastName.ShouldBe("B. Lovelace");
    }

    [Fact]
    public async Task EnsurePersonAsync_ExistingActiveRowDifferingByCaseAndWhitespace_ReportsExistingWithoutWriting()
    {
        // Arrange — the DB row is lower-case; the request arrives padded and upper-cased.
        using var context = CreateContext();
        var edjeId = Guid.NewGuid();
        context.People.Add(new Person
        {
            Id = Guid.NewGuid(),
            EdjeId = edjeId,
            Email = "ada@leadingedje.com",
            FirstName = "Ada",
            LastName = "Lovelace",
            IsActive = true,
            Source = null,
        });
        var ct = TestContext.Current.CancellationToken;
        await context.SaveChangesAsync(ct);
        var service = CreateService(context);

        // Act
        var result = await service.EnsurePersonAsync("  ADA@LeadingEDJE.com  ", "Ada Lovelace", Source);

        // Assert — matched case-insensitively; no duplicate; provenance of an imported row untouched.
        result.Outcome.ShouldBe(PersonProvisionOutcome.Existing);
        result.EdjeId.ShouldBe(edjeId);
        (await context.People.CountAsync(ct)).ShouldBe(1);
        (await context.People.SingleAsync(ct)).Source.ShouldBeNull();
    }

    [Fact]
    public async Task EnsurePersonAsync_ExistingInactiveRow_ReportsInactiveAndNeverReactivatesOrDuplicates()
    {
        // Arrange — D-06: deactivation is deliberate and must never be undone.
        using var context = CreateContext();
        var edjeId = Guid.NewGuid();
        context.People.Add(new Person
        {
            Id = Guid.NewGuid(),
            EdjeId = edjeId,
            Email = "erin@leadingedje.com",
            IsActive = false,
        });
        var ct = TestContext.Current.CancellationToken;
        await context.SaveChangesAsync(ct);
        var service = CreateService(context);

        // Act
        var result = await service.EnsurePersonAsync("erin@leadingedje.com", "Erin Ghost", Source);

        // Assert
        result.Outcome.ShouldBe(PersonProvisionOutcome.Inactive);
        (await context.People.CountAsync(ct)).ShouldBe(1);
        var person = await context.People.SingleAsync(ct);
        person.IsActive.ShouldBeFalse();
        person.EdjeId.ShouldBe(edjeId);
    }

    [Fact]
    public async Task EnsurePersonAsync_ExistingInactiveRowWithNullEdjeId_StillReportsInactiveAndMintsNothing()
    {
        // Arrange — the case GetEmployeeByEmailAsync collapses to null: without this branch the deny
        // path would silently become an insert (violating both D-06 and the LOWER(email) unique index).
        using var context = CreateContext();
        context.People.Add(new Person
        {
            Id = Guid.NewGuid(),
            EdjeId = null,
            Email = "gwen@leadingedje.com",
            IsActive = false,
        });
        var ct = TestContext.Current.CancellationToken;
        await context.SaveChangesAsync(ct);
        var service = CreateService(context);

        // Act
        var result = await service.EnsurePersonAsync("gwen@leadingedje.com", "Gwen Null", Source);

        // Assert
        result.Outcome.ShouldBe(PersonProvisionOutcome.Inactive);
        (await context.People.CountAsync(ct)).ShouldBe(1);
        var person = await context.People.SingleAsync(ct);
        person.IsActive.ShouldBeFalse();
        person.EdjeId.ShouldBeNull();
    }

    [Fact]
    public async Task EnsurePersonAsync_ExistingActiveRowWithNullEdjeId_MintsInPlaceWithoutDuplicating()
    {
        // Arrange
        using var context = CreateContext();
        var personId = Guid.NewGuid();
        context.People.Add(new Person
        {
            Id = personId,
            EdjeId = null,
            Email = "gwen@leadingedje.com",
            IsActive = true,
        });
        var ct = TestContext.Current.CancellationToken;
        await context.SaveChangesAsync(ct);
        var service = CreateService(context);

        // Act
        var result = await service.EnsurePersonAsync("gwen@leadingedje.com", "Gwen Null", Source);

        // Assert — minted to the row's own Id (TpsImportService.MintEdjeIds fallback semantics).
        result.Outcome.ShouldBe(PersonProvisionOutcome.Existing);
        result.EdjeId.ShouldBe(personId);
        (await context.People.CountAsync(ct)).ShouldBe(1);
        (await context.People.SingleAsync(ct)).EdjeId.ShouldBe(personId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EnsurePersonAsync_BlankEmail_ReportsRejectedAndWritesNothing(string? email)
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await service.EnsurePersonAsync(email, "Someone", Source);

        // Assert
        result.Outcome.ShouldBe(PersonProvisionOutcome.Rejected);
        (await context.People.CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task EnsurePersonAsync_CalledTwiceForSameEmail_IsIdempotent()
    {
        // Arrange
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var first = await service.EnsurePersonAsync("ada@leadingedje.com", "Ada Lovelace", Source);
        var second = await service.EnsurePersonAsync("ada@leadingedje.com", "Ada Lovelace", Source);

        // Assert
        first.Outcome.ShouldBe(PersonProvisionOutcome.Created);
        second.Outcome.ShouldBe(PersonProvisionOutcome.Existing);
        second.EdjeId.ShouldBe(first.EdjeId);
        (await context.People.CountAsync(ct)).ShouldBe(1);
    }

    // ---- Display-name persistence (smoke-test finding P2, 2026-07-30) ------------------------------
    // The bug: BootstrapSuperAdminSeeder creates rows with `displayName: null`, so FirstName/LastName are
    // null. On first SAML sign-in the directory lookup then returns `DisplayName = email` (its own
    // blank-name fallback), which is NON-EMPTY -- so SignInService.BuildIdentity's "fall back to the
    // assertion name" branch was unreachable and the real Google name was silently discarded. Nothing ever
    // wrote a name: Admin -> People listed every bootstrap admin with a blank NAME while the shell greeted
    // "Welcome, avery.quinn@example.com".
    //
    // Rules under test: a nameless row takes the assertion name; a name the APP derived (provenance
    // `Source` non-null) yields to the authoritative assertion name; an HR-authoritative row (`Source`
    // null -- imported/absorbed or admin CRUD) is NEVER overwritten; and no call fabricates a name when
    // the assertion supplies none (D-08).

    private static async Task<Guid> SeedPersonAsync(
        LeapDbContext context,
        string email,
        string? firstName,
        string? lastName,
        string? source,
        CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        context.People.Add(new Person
        {
            Id = id,
            EdjeId = id,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            IsActive = true,
            Source = source,
        });
        await context.SaveChangesAsync(ct);
        return id;
    }

    [Fact]
    public async Task TryFillMissingDisplayNameAsync_NamelessRow_FillsIt()
    {
        // Arrange — the seeder's path. Distinct from EnsureDisplayNameAsync: a derived placeholder must
        // only ever FILL a blank, never replace anything, because the seeder re-runs on every pod start.
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;
        var edjeId = await SeedPersonAsync(
            context, "avery.quinn@example.com", null, null, PersonSource.BootstrapSeed, ct);

        // Act
        await service.TryFillMissingDisplayNameAsync(edjeId, "Avery Quinn");

        // Assert
        var person = await context.People.SingleAsync(p => p.EdjeId == edjeId, ct);
        person.FirstName.ShouldBe("Avery");
        person.LastName.ShouldBe("Quinn");
    }

    [Fact]
    public async Task TryFillMissingDisplayNameAsync_RowAlreadyNamed_LeavesItAlone()
    {
        // Arrange — a name the SAML assertion already persisted. The seeder runs on every pod start, so
        // overwriting here would clobber the real name with a guess on every restart.
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;
        var edjeId = await SeedPersonAsync(
            context, "avery.quinn@example.com", "Benjamin", "Alvarez-Diaz",
            PersonSource.BootstrapSeed, ct);

        // Act
        await service.TryFillMissingDisplayNameAsync(edjeId, "Avery Quinn");

        // Assert
        var person = await context.People.SingleAsync(p => p.EdjeId == edjeId, ct);
        person.FirstName.ShouldBe("Benjamin");
        person.LastName.ShouldBe("Alvarez-Diaz");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryFillMissingDisplayNameAsync_NoCandidate_WritesNothing(string? candidate)
    {
        // Arrange — an underivable email yields no candidate; the row stays identity-only (D-08).
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;
        var edjeId = await SeedPersonAsync(
            context, "jkirk@leadingedje.com", null, null, PersonSource.BootstrapSeed, ct);

        // Act
        await service.TryFillMissingDisplayNameAsync(edjeId, candidate);

        // Assert
        var person = await context.People.SingleAsync(p => p.EdjeId == edjeId, ct);
        person.FirstName.ShouldBeNull();
        person.LastName.ShouldBeNull();
    }

    [Fact]
    public async Task TryFillMissingDisplayNameAsync_InactiveOrUnknownRow_WritesNothing()
    {
        // Arrange — D-06: a deactivated row is inert; an unknown EdjeId must not throw.
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.CreateVersion7();
        context.People.Add(new Person
        {
            Id = id,
            EdjeId = id,
            Email = "dana.former@example.test",
            IsActive = false,
            Source = PersonSource.BootstrapSeed,
        });
        await context.SaveChangesAsync(ct);

        // Act
        await service.TryFillMissingDisplayNameAsync(id, "Dana Former");
        await service.TryFillMissingDisplayNameAsync(Guid.CreateVersion7(), "Ghost User");

        // Assert
        var person = await context.People.SingleAsync(p => p.EdjeId == id, ct);
        person.FirstName.ShouldBeNull();
        (await context.People.CountAsync(ct)).ShouldBe(1);
    }

    [Fact]
    public async Task EnsureDisplayNameAsync_NamelessRow_PersistsAssertionNameAndReturnsIt()
    {
        // Arrange — exactly the deployed state: a bootstrap-seeded row with no name.
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;
        var edjeId = await SeedPersonAsync(
            context, "avery.quinn@example.com", null, null, PersonSource.BootstrapSeed, ct);

        // Act
        var displayName = await service.EnsureDisplayNameAsync(
            edjeId, "Avery Quinn", "avery.quinn@example.com");

        // Assert — returned for the session claim AND persisted for Admin -> People.
        displayName.ShouldBe("Avery Quinn");
        var person = await context.People.SingleAsync(p => p.EdjeId == edjeId, ct);
        person.FirstName.ShouldBe("Avery");
        person.LastName.ShouldBe("Quinn");
    }

    [Fact]
    public async Task EnsureDisplayNameAsync_AppMintedRowWithDifferentName_AssertionNameWins()
    {
        // Arrange — a name the seeder DERIVED from the email local part is a guess; the assertion is not.
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;
        var edjeId = await SeedPersonAsync(
            context, "n.brightwater@leadingedje.com", "N", "Brightwater", PersonSource.BootstrapSeed, ct);

        // Act
        var displayName = await service.EnsureDisplayNameAsync(
            edjeId, "Nova Brightwater", "n.brightwater@leadingedje.com");

        // Assert
        displayName.ShouldBe("Nova Brightwater");
        var person = await context.People.SingleAsync(p => p.EdjeId == edjeId, ct);
        person.FirstName.ShouldBe("Nova");
        person.LastName.ShouldBe("Brightwater");
    }

    [Fact]
    public async Task EnsureDisplayNameAsync_HrAuthoritativeRow_NeverOverwritesTheName()
    {
        // Arrange — Source null means imported/absorbed from the directory, or set via admin CRUD. HR
        // owns that name; a Google display name must not silently rewrite the system of record.
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;
        var edjeId = await SeedPersonAsync(
            context, "blair.dev@example.test", "Blair", "Dev", source: null, ct);

        // Act
        var displayName = await service.EnsureDisplayNameAsync(
            edjeId, "Blair Nickname Dev", "blair.dev@example.test");

        // Assert — the stored HR name is returned and left untouched.
        displayName.ShouldBe("Blair Dev");
        var person = await context.People.SingleAsync(p => p.EdjeId == edjeId, ct);
        person.FirstName.ShouldBe("Blair");
        person.LastName.ShouldBe("Dev");
    }

    [Fact]
    public async Task EnsureDisplayNameAsync_HrAuthoritativeRowWithNoName_StillTakesTheAssertionName()
    {
        // Arrange — an imported row can legitimately carry identity only (D-08). Filling a BLANK name is
        // additive, not a rewrite, so it is allowed regardless of provenance.
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;
        var edjeId = await SeedPersonAsync(
            context, "gwen.null@example.test", null, null, source: null, ct);

        // Act
        var displayName = await service.EnsureDisplayNameAsync(
            edjeId, "Gwen Null", "gwen.null@example.test");

        // Assert
        displayName.ShouldBe("Gwen Null");
        var person = await context.People.SingleAsync(p => p.EdjeId == edjeId, ct);
        person.FirstName.ShouldBe("Gwen");
        person.LastName.ShouldBe("Null");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EnsureDisplayNameAsync_NoAssertionName_FabricatesNothingAndFallsBackToEmail(
        string? assertionName)
    {
        // Arrange — D-08: no assertion name means identity only. Never invent one here.
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;
        var edjeId = await SeedPersonAsync(
            context, "nameless@leadingedje.com", null, null, PersonSource.SamlAutoCreate, ct);

        // Act
        var displayName = await service.EnsureDisplayNameAsync(
            edjeId, assertionName, "nameless@leadingedje.com");

        // Assert
        displayName.ShouldBe("nameless@leadingedje.com");
        var person = await context.People.SingleAsync(p => p.EdjeId == edjeId, ct);
        person.FirstName.ShouldBeNull();
        person.LastName.ShouldBeNull();
    }

    [Fact]
    public async Task EnsureDisplayNameAsync_AssertionMatchesStoredName_WritesNothing()
    {
        // Arrange — the steady state on every sign-in after the first. Must not churn the row.
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;
        var edjeId = await SeedPersonAsync(
            context, "avery.quinn@example.com", "Avery", "Quinn", PersonSource.BootstrapSeed, ct);

        // Act
        var displayName = await service.EnsureDisplayNameAsync(
            edjeId, "Avery Quinn", "avery.quinn@example.com");

        // Assert
        displayName.ShouldBe("Avery Quinn");
        var person = await context.People.SingleAsync(p => p.EdjeId == edjeId, ct);
        person.FirstName.ShouldBe("Avery");
        person.LastName.ShouldBe("Quinn");
    }

    [Fact]
    public async Task EnsureDisplayNameAsync_UnknownEdjeId_IsANoOpAndKeepsTheCallersResolvedName()
    {
        // Arrange — no row means nothing to reconcile and nothing to persist, so the caller's own
        // directory lookup stays authoritative. This is the shape the SAML integration tests exercise
        // (their directory is a stub with no backing `people` row): the resolved name must survive, or
        // this method would silently downgrade every such session's display name to the assertion.
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var displayName = await service.EnsureDisplayNameAsync(
            Guid.CreateVersion7(), "Assertion Name", "Directory Name");

        // Assert
        displayName.ShouldBe("Directory Name");
        (await context.People.CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task EnsureDisplayNameAsync_InactiveRow_WritesNothing()
    {
        // Arrange — D-06: a deactivated person is inert. Sign-in denies them anyway; do not touch the row.
        using var context = CreateContext();
        var service = CreateService(context);
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.CreateVersion7();
        context.People.Add(new Person
        {
            Id = id,
            EdjeId = id,
            Email = "dana.former@example.test",
            IsActive = false,
            Source = PersonSource.BootstrapSeed,
        });
        await context.SaveChangesAsync(ct);

        // Act
        var displayName = await service.EnsureDisplayNameAsync(
            id, "Dana Former", "dana.former@example.test");

        // Assert
        displayName.ShouldBe("Dana Former");
        var person = await context.People.SingleAsync(p => p.EdjeId == id, ct);
        person.FirstName.ShouldBeNull();
        person.LastName.ShouldBeNull();
    }
}
