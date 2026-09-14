using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// The <see cref="IClientStatusDerivation"/> predicates against real PostgreSQL — feature 007,
/// T011 (contract <c>date-predicates.md</c>), plus issue #274's
/// <see cref="IClientStatusDerivation.HasEverBeenAssigned"/>.
/// </summary>
/// <remarks>
/// <para>
/// This file is the only coverage a compiling predicate cannot fake. The unit tests in
/// <c>ClientStatusDerivationTests.cs</c> run against LINQ-to-Objects, which evaluates the same
/// expression tree in .NET — it cannot distinguish a predicate that translates to SQL from one that
/// compiles, passes every in-memory case, and throws <c>InvalidOperationException</c> the first time
/// Npgsql tries to render it. This exact shape shipped once already in this module.
/// </para>
/// <para>
/// <see cref="IClientStatusDerivation.HasNoFollowOn"/> is the one most at risk: it walks a navigation
/// (<c>sow.ClientAssignment.Sows</c>) inside a query rather than comparing a scalar, which is a
/// materially different translation path from the scalar comparisons.
/// </para>
/// <para>
/// <see cref="IClientStatusDerivation.HasEverBeenAssigned"/> (issue #274) is on that same navigation
/// path — a bare <c>Any()</c> over <c>Client.ClientAssignments</c>, which must render as a correlated
/// EXISTS. It is the one predicate whose result LINQ-to-Objects gets right for a completely different
/// reason than SQL does: in memory the navigation collection is whatever the fixture assigned, while
/// in the database it is a join. A predicate that read the collection eagerly would pass every unit
/// test and answer 500 on four shipped surfaces.
/// </para>
/// </remarks>
public class ClientStatusDerivationTranslationTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private const int EmployeeTypeId = 1;
    private const int EmployeeId = 1;
    private const int ClientId = 1;

    /// <summary>A client with no assignment row at all — Inactive (issue #274).</summary>
    private const int NeverAssignedClientId = 2;

    /// <summary>A client whose only assignment ended long ago — Former (issue #274).</summary>
    private const int EndedClientId = 3;

    private static readonly DateOnly Today = new(2026, 8, 17);

    [Fact]
    public async Task IsCurrent_TranslatesAndAppliesTheInclusiveTodayBoundary()
    {
        // Arrange
        await ResetDatabaseAsync();
        var openEnded = await SeedAssignmentAsync(id: 1, startDate: Today.AddMonths(-6), endDate: null);
        var endsToday = await SeedAssignmentAsync(id: 2, startDate: Today.AddMonths(-6), endDate: Today);
        var ended = await SeedAssignmentAsync(
            id: 3, startDate: Today.AddMonths(-6), endDate: Today.AddDays(-1));

        // Act
        var currentIds = await QueryAsync<ClientAssignment, int>(
            (context, derivation) => context.Set<ClientAssignment>()
                .Where(derivation.IsCurrent(Today))
                .Select(a => a.Id));

        // Assert
        currentIds.ShouldBe([openEnded, endsToday], ignoreOrder: true);
        currentIds.ShouldNotContain(ended);
    }

    [Fact]
    public async Task IsFutureDated_TranslatesAndAgreesWithIsCurrent_OnTheExactlyTodayCase()
    {
        // Arrange
        await ResetDatabaseAsync();
        var openEnded = await SeedAssignmentAsync(id: 1, startDate: Today.AddMonths(-6), endDate: null);
        var endsToday = await SeedAssignmentAsync(id: 2, startDate: Today.AddMonths(-6), endDate: Today);
        var endsTomorrow = await SeedAssignmentAsync(
            id: 3, startDate: Today.AddMonths(-6), endDate: Today.AddDays(1));

        // Act
        var futureDatedIds = await QueryAsync<ClientAssignment, int>(
            (context, derivation) => context.Set<ClientAssignment>()
                .Where(derivation.IsFutureDated(Today))
                .Select(a => a.Id));

        // Assert — the open-ended assignment has no end date, so it is current but never rolling out.
        futureDatedIds.ShouldBe([endsToday, endsTomorrow], ignoreOrder: true);
        futureDatedIds.ShouldNotContain(openEnded);
    }

    [Fact]
    public async Task IsExpiringWithin_TranslatesAndAppliesTheInclusiveDay90Boundary()
    {
        // Arrange — one assignment PER SOW: `ex_sow_no_overlap_per_assignment` rejects overlapping
        // periods on the same assignment, and these four span overlapping ranges by construction.
        await ResetDatabaseAsync();
        var yesterday = await SeedSowAsync(
            id: 1, await SeedAssignmentAsync(1, Today.AddYears(-1), endDate: null), Today.AddDays(-1));
        var todayEnding = await SeedSowAsync(
            id: 2, await SeedAssignmentAsync(2, Today.AddYears(-1), endDate: null), Today);
        var day90 = await SeedSowAsync(
            id: 3, await SeedAssignmentAsync(3, Today.AddYears(-1), endDate: null), Today.AddDays(90));
        var day91 = await SeedSowAsync(
            id: 4, await SeedAssignmentAsync(4, Today.AddYears(-1), endDate: null), Today.AddDays(91));

        // Act
        var expiringIds = await QueryAsync<Sow, int>(
            (context, derivation) => context.Set<Sow>()
                .Where(derivation.IsExpiringWithin(Today, 90))
                .Select(s => s.Id));

        // Assert
        expiringIds.ShouldBe([todayEnding, day90], ignoreOrder: true);
        expiringIds.ShouldNotContain(yesterday);
        expiringIds.ShouldNotContain(day91);
    }

    [Fact]
    public async Task HasEverBeenAssigned_TranslatesToACorrelatedExists_AndReadsNoDates()
    {
        // Arrange — three clients spanning the whole of what #274 needs to tell apart: never assigned,
        // assigned and still current, assigned and long finished.
        await ResetDatabaseAsync();
        await SeedClientAsync(NeverAssignedClientId, "Never Assigned");
        await SeedClientAsync(EndedClientId, "All Ended");
        await SeedAssignmentAsync(id: 1, startDate: Today.AddMonths(-6), endDate: null);
        await SeedAssignmentAsync(
            id: 2,
            startDate: Today.AddYears(-3),
            endDate: Today.AddYears(-2),
            clientId: EndedClientId);

        // Act
        var everAssigned = await QueryAsync<Client, int>(
            (context, derivation) => context.Set<Client>()
                .Where(derivation.HasEverBeenAssigned())
                .Select(c => c.Id));

        // Assert
        everAssigned.ShouldBe([ClientId, EndedClientId], ignoreOrder: true);
        everAssigned.ShouldNotContain(
            NeverAssignedClientId,
            "a client with no assignment row has never been assigned — this is what makes it Inactive "
                + "rather than Former (issue #274)");
        everAssigned.ShouldContain(
            EndedClientId,
            "an assignment that ENDED still counts: the predicate asks whether the client was ever "
                + "engaged, never whether it is engaged now. Reading a date here would make this a "
                + "second currency derivation, which BR-11 forbids");
    }

    [Fact]
    public async Task HasEverBeenAssigned_ComposesWithIsActive_ToNameAllThreeStatusesInSql()
    {
        // The shape every consumer actually uses (#274): two set-wise id queries, then StatusOfClient
        // over the pair. Asserted against real PostgreSQL because that composition is where a
        // non-translatable predicate would surface — and it is how all five surfaces derive status.
        await ResetDatabaseAsync();
        await SeedClientAsync(NeverAssignedClientId, "Never Assigned");
        await SeedClientAsync(EndedClientId, "All Ended");
        await SeedAssignmentAsync(id: 1, startDate: Today.AddMonths(-6), endDate: null);
        await SeedAssignmentAsync(
            id: 2,
            startDate: Today.AddYears(-3),
            endDate: Today.AddYears(-2),
            clientId: EndedClientId);

        var active = await QueryAsync<Client, int>(
            (context, derivation) => context.Set<Client>()
                .Where(derivation.IsActive(Today))
                .Select(c => c.Id));

        var everAssigned = await QueryAsync<Client, int>(
            (context, derivation) => context.Set<Client>()
                .Where(derivation.HasEverBeenAssigned())
                .Select(c => c.Id));

        using var scope = Services.CreateScope();
        var derivation = scope.ServiceProvider.GetRequiredService<IClientStatusDerivation>();

        string StatusOf(int clientId) =>
            derivation
                .StatusOfClient(active.Contains(clientId), everAssigned.Contains(clientId))
                .ToString();

        StatusOf(ClientId).ShouldBe("Active");
        StatusOf(EndedClientId).ShouldBe("Former");
        StatusOf(NeverAssignedClientId).ShouldBe("Inactive");
    }

    [Fact]
    public async Task HasNoFollowOn_TranslatesTheCorrelatedNavigation_NotJustAScalarComparison()
    {
        // Arrange — a SOW with a later follow-on, one whose only sibling is a PRIOR period, and one
        // with two prior siblings. The navigation walk is the part LINQ-to-Objects cannot vouch for.
        await ResetDatabaseAsync();
        var withFollowOn = await SeedAssignmentAsync(id: 1, startDate: Today.AddYears(-1), endDate: null);
        var current1 = await SeedSowAsync(id: 1, withFollowOn, Today.AddDays(30));
        await SeedSowAsync(id: 2, withFollowOn, Today.AddDays(60), startDate: Today.AddDays(31));

        var priorOnly = await SeedAssignmentAsync(id: 2, startDate: Today.AddYears(-1), endDate: null);
        await SeedSowAsync(id: 3, priorOnly, Today.AddDays(-31), startDate: Today.AddDays(-60));
        var current2 = await SeedSowAsync(id: 4, priorOnly, Today.AddDays(30), startDate: Today.AddDays(-30));

        var noSiblings = await SeedAssignmentAsync(id: 3, startDate: Today.AddYears(-1), endDate: null);
        var current3 = await SeedSowAsync(id: 5, noSiblings, Today.AddDays(30));

        // Act
        var hasNoFollowOnIds = await QueryAsync<Sow, int>(
            (context, derivation) => context.Set<Sow>()
                .Where(derivation.HasNoFollowOn())
                .Select(s => s.Id));

        // Assert
        hasNoFollowOnIds.ShouldNotContain(current1, "a later SOW exists on the same assignment");
        hasNoFollowOnIds.ShouldContain(current2, "the only sibling is a PRIOR period, not a follow-on");
        hasNoFollowOnIds.ShouldContain(current3, "no sibling at all");
    }

    [Fact]
    public async Task SowAssignmentIsOpenEndedAndActive_TranslatesTheNavigationEndAndStartComparison()
    {
        // Arrange (issue #457) — three SOWs whose assignments differ only in the two facets the
        // predicate reads: open-ended and started (kept), open-ended but future-dated (dropped), and
        // started but end-dated (dropped). The walk is `sow.ClientAssignment.EndDate`/`.StartDate`,
        // which LINQ-to-Objects evaluates in memory and cannot prove reaches Npgsql.
        await ResetDatabaseAsync();
        var openEndedActive = await SeedSowAsync(
            id: 1, await SeedAssignmentAsync(1, Today.AddMonths(-6), endDate: null), Today.AddDays(30));
        var openEndedFuture = await SeedSowAsync(
            id: 2, await SeedAssignmentAsync(2, Today.AddDays(1), endDate: null), Today.AddDays(30));
        var endDated = await SeedSowAsync(
            id: 3, await SeedAssignmentAsync(3, Today.AddMonths(-6), endDate: Today.AddDays(30)),
            Today.AddDays(30));

        // Act
        var qualifyingIds = await QueryAsync<Sow, int>(
            (context, derivation) => context.Set<Sow>()
                .Where(derivation.SowAssignmentIsOpenEndedAndActive(Today))
                .Select(s => s.Id));

        // Assert
        qualifyingIds.ShouldBe([openEndedActive]);
        qualifyingIds.ShouldNotContain(openEndedFuture, "a not-yet-started assignment is not active");
        qualifyingIds.ShouldNotContain(endDated, "an end-dated assignment is a planned rollout (issue #457)");
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>
    /// Runs a query built from the real <see cref="ClientStatusDerivation"/> against the shared
    /// Testcontainers database, proving the expression actually reaches Npgsql rather than throwing.
    /// </summary>
    private async Task<List<TResult>> QueryAsync<TEntity, TResult>(
        Func<LeapDbContext, IClientStatusDerivation, IQueryable<TResult>> query)
        where TEntity : class
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var derivation = scope.ServiceProvider.GetRequiredService<IClientStatusDerivation>();

        return await query(context, derivation).ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> SeedAssignmentAsync(
        int id, DateOnly startDate, DateOnly? endDate, int? clientId = null)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        await EnsureDirectoryFixturesAsync(context);

        context.Set<ClientAssignment>().Add(new ClientAssignment
        {
            Id = id,
            EmployeeId = EmployeeId,
            ClientId = clientId ?? ClientId,
            StartDate = startDate,
            EndDate = endDate,
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    /// <summary>
    /// Adds a client with no assignments, so <c>HasEverBeenAssigned</c> has something to exclude.
    /// </summary>
    /// <remarks>
    /// <c>EnsureDirectoryFixturesAsync</c> creates <see cref="ClientId"/> only when the table is
    /// EMPTY, so a client seeded here first would suppress it. Called before the assignment seeds in
    /// both #274 tests, which is why each of them names <see cref="ClientId"/> explicitly.
    /// </remarks>
    private async Task SeedClientAsync(int id, string name)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        await EnsureDirectoryFixturesAsync(context);

        context.Set<Client>().Add(new Client { Id = id, ClientName = name });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> SeedSowAsync(
        int id, int clientAssignmentId, DateOnly endDate, DateOnly? startDate = null)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.Set<Sow>().Add(new Sow
        {
            Id = id,
            ClientAssignmentId = clientAssignmentId,
            SowStartDate = startDate ?? endDate.AddDays(-30),
            SowEndDate = endDate,
            HasPassedApplicationValidation = true,
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private async Task EnsureDirectoryFixturesAsync(LeapDbContext context)
    {
        if (!await context.Set<EmployeeType>().AnyAsync(TestContext.Current.CancellationToken))
        {
            context.Set<EmployeeType>().Add(
                new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        }

        if (!await context.Set<Employee>().AnyAsync(TestContext.Current.CancellationToken))
        {
            context.Set<Employee>().Add(new Employee
            {
                Id = EmployeeId,
                FirstName = "Ada",
                LastName = "Translation",
                Email = "ada.translation@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = EmployeeTypeId,
                HireDate = Today.AddYears(-2),
                IsActive = true,
            });
        }

        if (!await context.Set<Client>().AnyAsync(TestContext.Current.CancellationToken))
        {
            context.Set<Client>().Add(new Client { Id = ClientId, ClientName = "Translation Client" });
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
