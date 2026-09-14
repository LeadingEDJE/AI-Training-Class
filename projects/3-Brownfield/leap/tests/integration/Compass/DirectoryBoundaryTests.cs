using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// Proves the Compass directory boundary resolves from the COMPASS store — <c>compass.employee</c> —
/// rather than from the legacy directory tables it was originally wired to.
/// </summary>
/// <remarks>
/// <para>
/// Why this test exists. <c>IDirectory</c> is documented as the Compass directory boundary,
/// but until this release <c>CompassDirectoryRepository</c> resolved it against
/// <c>context.Employees</c> — that is <c>public.employees</c>, a table the TIMESHEET module owns. The
/// 97 rows in <c>compass.employee</c> were unreachable through the contract built to serve them. A
/// test asserting only "the boundary returns an employee" would have passed throughout, which is why
/// the second test below seeds BOTH stores and pins which one answers.
/// </para>
/// <para>
/// The identifier is an <c>int</c> (owner decision D-2). Compass records are referenced across
/// the platform by their native integer keys. The previous <c>Guid</c> parameter was the legacy
/// directory's preserved uuid primary key; <c>compass.employee</c> has no such column, so the
/// signature could not survive the re-point.
/// </para>
/// </remarks>
public class DirectoryBoundaryTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private const int EmployeeTypeId = 1;

    /// <summary>Seeds one Compass employee and returns its identifier.</summary>
    /// <remarks>
    /// <paramref name="email"/>, <paramref name="timezone"/> and <paramref name="isDeliveryTeam"/> are
    /// overridable because the defaults cannot exercise the boundary: the default address is already
    /// lower-cased and trimmed, so it proves nothing about case-insensitive matching; the default
    /// timezone is the entity's Eastern initialiser, the value every fixture here uses, which is
    /// exactly why a wrong zone stayed invisible (feature 018 research D-11); and the delivery flag
    /// defaults true, so only an override shows a stored false crossing the boundary.
    /// </remarks>
    private async Task<int> SeedCompassEmployeeAsync(
        int employeeId,
        string firstName,
        string lastName,
        bool isActive = true,
        string? email = null,
        string? timezone = null,
        bool isDeliveryTeam = true)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (!await context.Set<EmployeeType>().AnyAsync(TestContext.Current.CancellationToken))
        {
            context.Set<EmployeeType>().Add(new EmployeeType
            {
                Id = EmployeeTypeId,
                TypeName = "Full Time",
                IsActive = true,
            });
        }

        context.Set<Employee>().Add(new Employee
        {
            Id = employeeId,
            FirstName = firstName,
            LastName = lastName,
            Email = email ?? $"{firstName}.{lastName}@example.test".ToLowerInvariant(),
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = isActive,
            StateOfResidence = "OH",
            Timezone = timezone ?? UsTimeZones.Default,
            IsDeliveryTeam = isDeliveryTeam,
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return employeeId;
    }

    /// <summary>Seeds one Compass client (issue #430).</summary>
    private async Task SeedCompassClientAsync(int clientId, string clientName)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.Set<Client>().Add(new Client
        {
            Id = clientId,
            ClientName = clientName,
            IsInternal = false,
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetEmployeeAsync_ReturnsTheCompassRecord_ForItsIntegerIdentifier()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeId = await SeedCompassEmployeeAsync(42, "Ada", "Lovelace");

        // A bare DI scope has no HTTP context, and IDirectory now resolves the caller's tier
        // (spec 009 Slice 1) -- construct the service directly with a stub, per
        // NoHttpContextCurrentUser's remarks, rather than resolve IDirectory from DI.
        using var scope = Services.CreateScope();
        IDirectory directory = new CompassDirectoryService(
            scope.ServiceProvider.GetRequiredService<ICompassDirectoryRepository>(),
            new NoHttpContextCurrentUser());

        // Act
        var employee = await directory.GetEmployeeAsync(
            employeeId, TestContext.Current.CancellationToken);

        // Assert
        employee.ShouldNotBeNull();
        employee.Id.ShouldBe(employeeId);
        employee.DisplayName.ShouldBe("Ada Lovelace");
        employee.IsActive.ShouldBeTrue();
    }

    /// <summary>
    /// The client list issues 009's Slice-3-style set-wise queries against REAL PostgreSQL and
    /// translates (issue #430).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the coverage the unit suite cannot give. The EF Core InMemory provider translates
    /// nothing — it evaluates the same expression tree in .NET — so a query shape Npgsql cannot render
    /// passes every unit test and answers HTTP 500 in every real environment
    /// (the projected-member trap). This read constructs its
    /// named tuple only after materialisation and orders by an entity column, and this test is what
    /// establishes that "it translates" rather than asserting it in a comment.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheClientListRead_TranslatesAgainstRealPostgres()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedCompassClientAsync(1, "Acme Corp");
        await SeedCompassClientAsync(2, "Globex Corp");

        using var scope = Services.CreateScope();
        IDirectory directory = new CompassDirectoryService(
            scope.ServiceProvider.GetRequiredService<ICompassDirectoryRepository>(),
            new NoHttpContextCurrentUser());

        // Act
        var clients = await directory.GetClientsAsync(TestContext.Current.CancellationToken);

        // Assert — every client, each with a derived status from the shared derivation.
        clients.Select(c => c.Id).ShouldBe([1, 2]);
        clients.ShouldAllBe(c => c.Status == "Inactive");
    }

    /// <summary>
    /// A caller holding no Compass privilege reads the stored delivery-team flag in-process
    /// (FR-007, FR-008, SC-005).
    /// </summary>
    /// <remarks>
    /// In-process rather than over HTTP, deliberately. The versioned route requires the Compass
    /// admin policy, so a no-privilege caller there is refused with 403 and the claim cannot be
    /// shown at all. What is asserted is the payload a Baseline-tier caller receives, not who may
    /// reach the route. Both stored values are asserted: the entity defaults to <c>true</c> and the
    /// DTO member to <c>false</c>, so a <c>false</c>-only case passes against a projection that
    /// never writes it.
    /// </remarks>
    [Fact]
    public async Task GetEmployeeAsync_PublishesTheDeliveryTeamFlag_ToACallerHoldingNoCompassPrivilege()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedCompassEmployeeAsync(51, "Mary", "Jackson", isDeliveryTeam: true);
        await SeedCompassEmployeeAsync(52, "Dorothy", "Vaughan", isDeliveryTeam: false);

        using var scope = Services.CreateScope();
        IDirectory directory = new CompassDirectoryService(
            scope.ServiceProvider.GetRequiredService<ICompassDirectoryRepository>(),
            new NoHttpContextCurrentUser());

        // Act
        var onDelivery = await directory.GetEmployeeAsync(
            51, TestContext.Current.CancellationToken);
        var notOnDelivery = await directory.GetEmployeeAsync(
            52, TestContext.Current.CancellationToken);

        // Assert
        onDelivery.ShouldNotBeNull();
        onDelivery.IsDeliveryTeam.ShouldBeTrue();
        notOnDelivery.ShouldNotBeNull();
        notOnDelivery.IsDeliveryTeam.ShouldBeFalse();

        // The caller really did hold no Compass privilege: NoHttpContextCurrentUser resolves to the
        // Baseline tier, which is what withholds the gated group.
        onDelivery.TimeTracking.ShouldBeNull();
    }

    [Fact]
    public async Task GetEmployeeAsync_ReadsTheCompassStore_NotTheLegacyDirectory()
    {
        // Arrange — NO Compass employee carries the requested identifier. The legacy public.employees
        // table was dropped by DropLegacyDirectoryTables, so no fallback store exists; a lookup that
        // misses in compass.employee must return null.
        await ResetDatabaseAsync();
        await SeedCompassEmployeeAsync(7, "Grace", "Hopper");

        using var scope = Services.CreateScope();
        IDirectory directory = new CompassDirectoryService(
            scope.ServiceProvider.GetRequiredService<ICompassDirectoryRepository>(),
            new NoHttpContextCurrentUser());

        // Act
        var missing = await directory.GetEmployeeAsync(
            999, TestContext.Current.CancellationToken);
        var present = await directory.GetEmployeeAsync(
            7, TestContext.Current.CancellationToken);

        // Assert
        missing.ShouldBeNull("no compass.employee carries id 999, and the legacy directory is not a fallback");
        present.ShouldNotBeNull();
        present.DisplayName.ShouldBe("Grace Hopper");
    }

    // ---------------------------------------------- email-keyed reads (feature 018, issue #424)
    //
    // These are not redundant with the unit tests. The unit suite runs on the EF Core IN-MEMORY
    // provider, which TRANSLATES NOTHING -- it evaluates the same expression tree in .NET. So a
    // query shape PostgreSQL cannot translate passes every unit test and throws at runtime, and
    // this file is the only place that difference is visible. Each test below therefore has two
    // jobs: assert the value, and establish that the query RUNS AT ALL.

    [Fact]
    public async Task GetEmployeeByEmailAsync_TranslatesAndResolvesFromTheCompassStore()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedCompassEmployeeAsync(
            11, "Ada", "Lovelace", timezone: "America/Chicago");

        using var scope = Services.CreateScope();
        IDirectory directory = new CompassDirectoryService(
            scope.ServiceProvider.GetRequiredService<ICompassDirectoryRepository>(),
            new NoHttpContextCurrentUser());

        // Act
        var employee = await directory.GetEmployeeByEmailAsync(
            "ada.lovelace@example.test", TestContext.Current.CancellationToken);

        // Assert
        employee.ShouldNotBeNull();
        employee.Id.ShouldBe(11);
        employee.Timezone.ShouldBe("America/Chicago");
    }

    [Fact]
    public async Task GetEmployeeByEmailAsync_MatchesAcrossCaseAndWhitespace_OnBothSides()
    {
        // Arrange -- the stored value carries stray whitespace AND mixed case, and so does the
        // candidate. This is the case the unit suite cannot fully vouch for, because only the real
        // provider proves that Trim()/ToLower() render as btrim()/lower() rather than throwing --
        // and it is the case a candidate-only trim (CompassEmployeeRepository.EmailExistsAsync's
        // shape) silently fails.
        await ResetDatabaseAsync();
        await SeedCompassEmployeeAsync(
            12, "Grace", "Hopper",
            email: "  Grace.Hopper@Example.Test ",
            timezone: "America/Denver");

        using var scope = Services.CreateScope();
        IDirectory directory = new CompassDirectoryService(
            scope.ServiceProvider.GetRequiredService<ICompassDirectoryRepository>(),
            new NoHttpContextCurrentUser());

        // Act
        var employee = await directory.GetEmployeeByEmailAsync(
            "  GRACE.HOPPER@EXAMPLE.TEST  ", TestContext.Current.CancellationToken);

        // Assert
        employee.ShouldNotBeNull();
        employee.Id.ShouldBe(12);
        employee.Timezone.ShouldBe("America/Denver");
    }

    [Fact]
    public async Task GetEmployeeByEmailAsync_ReadsTheCompassStore_NotTheLegacyDirectory()
    {
        // Arrange -- Compass holds no employee at the requested address. The legacy directory tables
        // were dropped by DropLegacyDirectoryTables, so there is no fallback store to find them in;
        // an email that misses in compass.employee must return null. Mirrors the identifier-keyed test.
        await ResetDatabaseAsync();
        await SeedCompassEmployeeAsync(13, "Ada", "Lovelace");

        using var scope = Services.CreateScope();
        IDirectory directory = new CompassDirectoryService(
            scope.ServiceProvider.GetRequiredService<ICompassDirectoryRepository>(),
            new NoHttpContextCurrentUser());

        // Act
        var legacyOnly = await directory.GetEmployeeByEmailAsync(
            "katherine.johnson@example.test", TestContext.Current.CancellationToken);
        var inCompass = await directory.GetEmployeeByEmailAsync(
            "ada.lovelace@example.test", TestContext.Current.CancellationToken);

        // Assert
        legacyOnly.ShouldBeNull(
            "no compass.employee carries that address, and the legacy directory is not a fallback");
        inCompass.ShouldNotBeNull();
        inCompass.Id.ShouldBe(13);
    }

    [Fact]
    public async Task GetEmployeeByEmailAsync_WithABlankKey_ReturnsNullRatherThanAnArbitraryEmployee()
    {
        // Arrange -- fails closed against a real database, where a wildcard would actually have rows
        // to hand back. The unit test asserts the store is never touched; this one asserts that even
        // if it were, nothing comes out.
        await ResetDatabaseAsync();
        await SeedCompassEmployeeAsync(14, "Ada", "Lovelace");

        using var scope = Services.CreateScope();
        IDirectory directory = new CompassDirectoryService(
            scope.ServiceProvider.GetRequiredService<ICompassDirectoryRepository>(),
            new NoHttpContextCurrentUser());

        // Act
        var fromNull = await directory.GetEmployeeByEmailAsync(
            null, TestContext.Current.CancellationToken);
        var fromBlank = await directory.GetEmployeeByEmailAsync(
            "   ", TestContext.Current.CancellationToken);

        // Assert
        fromNull.ShouldBeNull();
        fromBlank.ShouldBeNull();
    }

    [Fact]
    public async Task GetEmployeesByEmailAsync_TranslatesTheBatchPredicate_AndResolvesPerRow()
    {
        // Arrange -- THE test this file exists for. The batch read's predicate is a collection
        // Contains over an expression (lower(btrim(email))); an implementation that filtered inside
        // a projection instead would pass all 51 unit tests and throw here.
        await ResetDatabaseAsync();
        await SeedCompassEmployeeAsync(21, "Ada", "Lovelace", timezone: "America/Chicago");
        await SeedCompassEmployeeAsync(22, "Grace", "Hopper", timezone: "America/Denver");
        await SeedCompassEmployeeAsync(23, "Katherine", "Goble", timezone: "Pacific/Honolulu");

        using var scope = Services.CreateScope();
        IDirectory directory = new CompassDirectoryService(
            scope.ServiceProvider.GetRequiredService<ICompassDirectoryRepository>(),
            new NoHttpContextCurrentUser());

        // Act -- mixed casing, padding, a duplicate spelling, and one address matching nothing.
        var employees = await directory.GetEmployeesByEmailAsync(
            [
                "ADA.LOVELACE@EXAMPLE.TEST",
                "  ada.lovelace@example.test  ",
                "grace.hopper@example.test",
                "katherine.goble@example.test",
                "nobody@example.test",
            ],
            TestContext.Current.CancellationToken);

        // Assert -- three distinct employees, each with ITS OWN zone. Three zones rather than two,
        // so a bug applying the first row's value to every row cannot pass by coincidence.
        employees.Count.ShouldBe(3);
        employees.Single(e => e.Id == 21).Timezone.ShouldBe("America/Chicago");
        employees.Single(e => e.Id == 22).Timezone.ShouldBe("America/Denver");
        employees.Single(e => e.Id == 23).Timezone.ShouldBe("Pacific/Honolulu");
    }

    [Fact]
    public async Task GetEmployeesByEmailAsync_WithNothingUsable_ReturnsEmptyAgainstARealDatabase()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedCompassEmployeeAsync(24, "Ada", "Lovelace");

        using var scope = Services.CreateScope();
        IDirectory directory = new CompassDirectoryService(
            scope.ServiceProvider.GetRequiredService<ICompassDirectoryRepository>(),
            new NoHttpContextCurrentUser());

        // Act
        var fromEmpty = await directory.GetEmployeesByEmailAsync(
            [], TestContext.Current.CancellationToken);
        var fromBlanks = await directory.GetEmployeesByEmailAsync(
            [null, "", "   "], TestContext.Current.CancellationToken);

        // Assert
        fromEmpty.ShouldBeEmpty();
        fromBlanks.ShouldBeEmpty();
    }

    [Fact]
    public async Task EmailKeyedReads_PublishTheCoachAndTypeNavigations_TheSameWayTheIdentifierReadDoes()
    {
        // Arrange -- the three reads share one query helper, and the failure this guards is silent:
        // a missing Include(e => e.Coach) does not throw, it publishes Coach: null, which reads as
        // "this EDJEr has no coach". Same for EmployeeType, which degrades to an empty string.
        await ResetDatabaseAsync();
        await SeedCompassEmployeeAsync(31, "Marcus", "Bellweather");

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var coached = new Employee
        {
            Id = 32,
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada.lovelace@example.test",
            HireDate = new DateOnly(2021, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            CoachEmployeeId = 31,
            IsActive = true,
            StateOfResidence = "IL",
            Timezone = "America/Chicago",
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        };
        context.Set<Employee>().Add(coached);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IDirectory directory = new CompassDirectoryService(
            scope.ServiceProvider.GetRequiredService<ICompassDirectoryRepository>(),
            new NoHttpContextCurrentUser());

        // Act -- all three entry points, on the same row.
        var byId = await directory.GetEmployeeAsync(32, TestContext.Current.CancellationToken);
        var byEmail = await directory.GetEmployeeByEmailAsync(
            "ada.lovelace@example.test", TestContext.Current.CancellationToken);
        var batch = await directory.GetEmployeesByEmailAsync(
            ["ada.lovelace@example.test"], TestContext.Current.CancellationToken);

        // Assert
        byId.ShouldNotBeNull();
        byId.Coach.ShouldNotBeNull().DisplayName.ShouldBe("Marcus Bellweather");
        byId.EmployeeType.ShouldBe("Full Time");

        byEmail.ShouldNotBeNull();
        byEmail.Coach.ShouldNotBeNull().DisplayName.ShouldBe("Marcus Bellweather");
        byEmail.EmployeeType.ShouldBe("Full Time");

        var fromBatch = batch.ShouldHaveSingleItem();
        fromBatch.Coach.ShouldNotBeNull().DisplayName.ShouldBe("Marcus Bellweather");
        fromBatch.EmployeeType.ShouldBe("Full Time");
    }
}
