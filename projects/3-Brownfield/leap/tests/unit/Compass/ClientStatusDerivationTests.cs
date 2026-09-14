using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The single shared derivation of client status and assignment currency.
/// </summary>
/// <remarks>
/// <para>
/// Read the contract, not AC-42's PRD wording. They disagree on the exactly-today case: PRD v11's
/// literal "inactive otherwise" would make it Inactive, and the owner decision the contract records
/// makes it Active for the whole calendar day (§ 4). That is the case AC-42 names as the one
/// independent implementations get wrong.
/// </para>
/// <para>
/// BR-7 ("current assignment": end date empty or not in the past) and BR-11 ("client is active") are
/// the SAME predicate, existentially quantified — a client is Active exactly when it holds at least
/// one current assignment. They are derived from one expression here so they cannot disagree.
/// </para>
/// </remarks>
public class ClientStatusDerivationTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private readonly ClientStatusDerivation _derivation = new();

    // ------------------------------------------------------------------ BR-7 — assignment currency

    [Fact]
    public void IsCurrent_NoEndDate_IsCurrent()
    {
        // Arrange / Act / Assert
        Evaluate(Assignment(endDate: null)).ShouldBeTrue();
    }

    [Fact]
    public void IsCurrent_EndDateInTheFuture_IsCurrent()
    {
        Evaluate(Assignment(Today.AddDays(30))).ShouldBeTrue();
    }

    [Fact]
    public void IsCurrent_EndDateExactlyToday_IsCurrent()
    {
        // The boundary the contract calls out. "Not in the past" is inclusive of today: an EDJEr is
        // assigned for the whole of their final working day, not until some point during it.
        Evaluate(Assignment(Today)).ShouldBeTrue();
    }

    [Fact]
    public void IsCurrent_EndDateYesterday_IsNotCurrent()
    {
        Evaluate(Assignment(Today.AddDays(-1))).ShouldBeFalse();
    }

    // ------------------------------------------------------------------ BR-11 — the five cases

    [Fact]
    public void IsActive_ClientWithNoAssignmentsAtAll_IsInactive()
    {
        // Case 1, the one most often got wrong. A brand-new client is Inactive from creation — and
        // simultaneously fully selectable for assignment, because status gates nothing.
        Evaluate(Client()).ShouldBeFalse();
        _derivation.Of(Client(), Today).ShouldBe(ClientStatus.Inactive);
    }

    [Fact]
    public void IsActive_AtLeastOneOpenEndedAssignment_IsActive()
    {
        var client = Client(Assignment(Today.AddDays(-400)), Assignment(endDate: null));

        Evaluate(client).ShouldBeTrue();
        _derivation.Of(client, Today).ShouldBe(ClientStatus.Active);
    }

    [Fact]
    public void IsActive_AtLeastOneAssignmentEndingInTheFuture_IsActive()
    {
        var client = Client(Assignment(Today.AddDays(90)));

        Evaluate(client).ShouldBeTrue();
        _derivation.Of(client, Today).ShouldBe(ClientStatus.Active);
    }

    [Fact]
    public void IsActive_LatestAssignmentEndsExactlyToday_IsActive()
    {
        // Case 4 — owner decision, superseding PRD v11's literal wording.
        var client = Client(Assignment(Today.AddDays(-200)), Assignment(Today));

        Evaluate(client).ShouldBeTrue();
        _derivation.Of(client, Today).ShouldBe(ClientStatus.Active);
    }

    [Fact]
    public void IsActive_AllAssignmentsEndedBeforeToday_IsFormer()
    {
        // Case 5, RE-SPLIT by issue #274. This read Inactive until the owner separated "we worked with
        // them and stopped" from "we set them up and never engaged" — two states this module used to
        // collapse into one word. `IsActive` is UNCHANGED; only the word for its false branch moved,
        // and which word it moves to now depends on whether any assignment has ever existed.
        var client = Client(Assignment(Today.AddDays(-30)), Assignment(Today.AddDays(-1)));

        Evaluate(client).ShouldBeFalse();
        _derivation.Of(client, Today).ShouldBe(ClientStatus.Former);
    }

    // ------------------------------------------------------------------ The value space itself

    [Fact]
    public void ClientStatus_HasExactlyTheThreeValuesTheSpecDefines()
    {
        // Issue #274 made this THREE-valued, and total — every client still resolves to exactly one,
        // with no Unknown, no None and no null. What the original binary rule was protecting is
        // unchanged: the Client Directory's status column stays sortable because the value space is
        // closed and every client has one, not because it has two members.
        //
        // Ordinals are declaration order, and Former is appended LAST on purpose. The value is never
        // persisted (see the test below), so the ordinals carry no meaning — but appending rather than
        // inserting keeps that true of any future reader who assumes otherwise.
        //
        // The enum carries no executable IL, so coverage tools emit no Cobertura entry for its file.
        // This test is what its per-file coverage exclusion rests on: the value space is asserted
        // directly rather than waived, exactly as SowType.cs's exclusion rests on
        // CompassEntityModelTests.SowType_HasExactlyTheThreeValuesTheSpecDefines.
        Enum.GetNames<ClientStatus>().ShouldBe(["Active", "Inactive", "Former"]);
    }

    [Fact]
    public void AssignmentStatus_HasExactlyTheTwoValuesAnAssignmentCanHold()
    {
        // The other half of issue #274, and the reason it is a SEPARATE enum. An assignment is current
        // or it is not; "Former" is a property of a CLIENT's history, not of one engagement. Sharing
        // one enum across both levels is what let `From(bool)` mean two different things at eight call
        // sites — and would let a client-level caller silently lose Former by reaching for the binary
        // naming method. Two types make that a compile error instead.
        //
        // The words are deliberately the SAME two strings: `AssignmentStatus.Active.ToString()` is
        // still "Active", which is what the specs, the frontend row types and the cross-surface
        // agreement fold all assert. This split changes types, never wire values.
        //
        // Pure enum, no instrumentable IL — this test is what its coverage exclusion in
        // scripts/check-coverage.sh rests on, exactly as ClientStatus's does.
        Enum.GetNames<AssignmentStatus>().ShouldBe(["Active", "Inactive"]);
    }

    [Fact]
    public void ClientStatus_IsNeverPersisted_SoItsOrdinalsCarryNoMeaning()
    {
        // Derived on every read and never stored (BR-11, and
        // CompassSchemaFromErdTests.Client_HasNoStoredStatusColumn). Recorded so nobody later
        // "stabilises" the ordinals for a database column that must never exist.
        typeof(Client).GetProperties()
            .ShouldNotContain(p => p.PropertyType == typeof(ClientStatus));
    }

    // ------------------------------------------------------------------ Three-valued and TOTAL

    [Fact]
    public void Of_EveryClientResolvesToExactlyOneOfThreeValues()
    {
        // Totality is the property that survived issue #274 unchanged. There is no Unknown and no
        // null for any arrangement of assignments — including none at all — which is what lets the
        // Client Directory sort the column.
        Client[] clients =
        [
            Client(),
            Client(Assignment(endDate: null)),
            Client(Assignment(Today)),
            Client(Assignment(Today.AddDays(-1))),
            Client(Assignment(Today.AddDays(5)), Assignment(Today.AddDays(-5))),
        ];

        foreach (var client in clients)
        {
            _derivation.Of(client, Today)
                .ShouldBeOneOf(ClientStatus.Active, ClientStatus.Inactive, ClientStatus.Former);
        }
    }

    // ------------------------------------------------------ #274 — the Inactive/Former split

    [Fact]
    public void HasEverBeenAssigned_ClientWithNoAssignmentsAtAll_IsFalse()
    {
        // The ONE new fact issue #274 needs. Everything else about the rule is unchanged.
        EvaluateHasEverBeenAssigned(Client()).ShouldBeFalse();
    }

    [Fact]
    public void HasEverBeenAssigned_ClientWhoseOnlyAssignmentEndedLongAgo_IsTrue()
    {
        // Deliberately an ENDED assignment: the predicate asks "has this client ever been engaged",
        // never "is it engaged now". Confusing the two collapses Former back into Inactive.
        EvaluateHasEverBeenAssigned(Client(Assignment(Today.AddDays(-900)))).ShouldBeTrue();
    }

    [Fact]
    public void HasEverBeenAssigned_LooksAtNoDates_SoItCannotDisagreeWithIsCurrent()
    {
        // Every end date, including the exactly-today boundary AC-42 names: the answer is the same
        // for all of them, because the predicate counts rows rather than comparing them to today.
        // A version written as "has an assignment that ended" would silently re-derive currency.
        DateOnly?[] candidates =
        [
            null,
            Today.AddDays(-10),
            Today.AddDays(-1),
            Today,
            Today.AddDays(1),
        ];

        foreach (var endDate in candidates)
        {
            EvaluateHasEverBeenAssigned(Client(Assignment(endDate)))
                .ShouldBeTrue(
                    $"end date {endDate?.ToString() ?? "null"} changed the answer — this predicate "
                        + "must not look at dates at all");
        }
    }

    [Fact]
    public void StatusOfClient_IsTheWholeTruthTable_AndTheImpossibleRowIsStillTotal()
    {
        // The naming function, asserted exhaustively rather than case by case. Three reachable rows
        // and one that cannot occur — a client holding a current assignment while never having been
        // assigned. It is named anyway, because a switch that threw or returned null there would make
        // the value space non-total, which is the property the sort depends on.
        _derivation.StatusOfClient(holdsCurrentAssignment: true, hasEverBeenAssigned: true)
            .ShouldBe(ClientStatus.Active);
        _derivation.StatusOfClient(holdsCurrentAssignment: false, hasEverBeenAssigned: true)
            .ShouldBe(ClientStatus.Former);
        _derivation.StatusOfClient(holdsCurrentAssignment: false, hasEverBeenAssigned: false)
            .ShouldBe(ClientStatus.Inactive);
        _derivation.StatusOfClient(holdsCurrentAssignment: true, hasEverBeenAssigned: false)
            .ShouldBe(
                ClientStatus.Active,
                "unreachable, but holding a current assignment must never read as never-engaged");
    }

    [Fact]
    public void StatusOfClient_AgreesWithOf_ForEveryArrangementOfAssignments()
    {
        // The two shapes the consumers actually use: lists compose the two predicates and call
        // StatusOfClient with the booleans; detail views call Of with a loaded client. Two screens
        // disagreeing about one client is the AC-42 failure, so the equivalence is asserted rather
        // than assumed — this is the same guarantee the pre-#274 code got from having ONE bool.
        DateOnly?[] candidates = [null, Today.AddDays(-10), Today.AddDays(-1), Today, Today.AddDays(10)];

        Client[] clients =
        [
            Client(),
            .. candidates.Select(d => Client(Assignment(d))),
            .. candidates.Select(d => Client(Assignment(d), Assignment(Today.AddDays(-900)))),
        ];

        foreach (var client in clients)
        {
            var viaBooleans = _derivation.StatusOfClient(
                Evaluate(client),
                EvaluateHasEverBeenAssigned(client));

            _derivation.Of(client, Today).ShouldBe(viaBooleans);
        }
    }

    [Fact]
    public void StatusOfAssignment_IsBinary_AndNeverNamesFormer()
    {
        // An assignment row is current or it is not. `Former` is not in AssignmentStatus at all, so
        // this is enforced by the type rather than by this assertion — which exists to record WHY the
        // second enum is there, and to fail if someone "simplifies" the two back into one.
        _derivation.StatusOfAssignment(isCurrent: true).ShouldBe(AssignmentStatus.Active);
        _derivation.StatusOfAssignment(isCurrent: false).ShouldBe(AssignmentStatus.Inactive);
    }

    [Fact]
    public void StatusOfAssignment_PublishesTheSameWordsItAlwaysDid()
    {
        // The wire contract. `ClientAssignmentHistoryDto.Status` and `EmployeeAssignmentDto.Status`
        // are strings built with .ToString(), and the frontend row types, the Playwright specs and
        // ClientStatusCrossSurfaceAgreementTests.FromAssignmentRows all match on these two literals.
        // Splitting the enum must not have changed them.
        _derivation.StatusOfAssignment(isCurrent: true).ToString().ShouldBe("Active");
        _derivation.StatusOfAssignment(isCurrent: false).ToString().ShouldBe("Inactive");
    }

    // ------------------------------------------------------------------ One expression, two shapes

    [Fact]
    public void IsActive_IsDefinedAsAnyIsCurrent_NotASecondComparison()
    {
        // The contract's § 2 requirement, asserted behaviourally: for every arrangement of end dates,
        // the client-level answer must equal "any assignment satisfies the assignment-level answer".
        // Two separately-written methods that agree today are the divergence AC-42 warns about,
        // arriving on schedule.
        DateOnly?[] candidates =
        [
            null,
            Today.AddDays(-10),
            Today.AddDays(-1),
            Today,
            Today.AddDays(1),
            Today.AddDays(10),
        ];

        foreach (var first in candidates)
        {
            foreach (var second in candidates)
            {
                var client = Client(Assignment(first), Assignment(second));
                var expected = client.ClientAssignments.Any(a => Evaluate(a));

                Evaluate(client).ShouldBe(expected);

                // Every client here HAS assignments, so the false branch is Former rather than
                // Inactive — #274 split that branch without touching IsActive, which is exactly what
                // this test is asserting about IsActive.
                _derivation.Of(client, Today)
                    .ShouldBe(expected ? ClientStatus.Active : ClientStatus.Former);
            }
        }
    }

    [Fact]
    public void SetWiseAndPerClientShapes_Agree()
    {
        // The Client Directory composes the expression into its query; the client view evaluates one
        // loaded client. Disagreement between them is a defect visible as two screens contradicting
        // each other about the same client.
        Client[] clients =
        [
            Client(),
            Client(Assignment(endDate: null)),
            Client(Assignment(Today)),
            Client(Assignment(Today.AddDays(-1))),
        ];

        var setWise = clients.AsQueryable().Where(_derivation.IsActive(Today)).ToList();

        foreach (var client in clients)
        {
            var perClient = _derivation.Of(client, Today) == ClientStatus.Active;
            setWise.Contains(client).ShouldBe(perClient);
        }
    }

    // ------------------------------------------------------------------ IsFutureDated (feature 007, FR-004)

    [Fact]
    public void IsFutureDated_NoEndDate_IsFalse()
    {
        // An open-ended assignment is current but is not "rolling out" — there is no date it ends.
        EvaluateFutureDated(Assignment(endDate: null)).ShouldBeFalse();
    }

    [Fact]
    public void IsFutureDated_EndDateYesterday_IsFalse()
    {
        EvaluateFutureDated(Assignment(Today.AddDays(-1))).ShouldBeFalse();
    }

    [Fact]
    public void IsFutureDated_EndDateExactlyToday_IsTrue()
    {
        // Must agree with IsCurrent on this boundary, or an EDJEr ending today would count as current
        // but not as rolling out (contract date-predicates.md).
        EvaluateFutureDated(Assignment(Today)).ShouldBeTrue();
    }

    [Fact]
    public void IsFutureDated_EndDateTomorrow_IsTrue()
    {
        EvaluateFutureDated(Assignment(Today.AddDays(1))).ShouldBeTrue();
    }

    [Fact]
    public void IsFutureDated_Implies_IsCurrent()
    {
        // The consistency property T006 requires: composed from IsCurrent so the two cannot drift on
        // the exactly-today case, asserted across a fixed date set rather than by inspection.
        DateOnly?[] candidates =
        [
            null,
            Today.AddDays(-10),
            Today.AddDays(-1),
            Today,
            Today.AddDays(1),
            Today.AddDays(10),
        ];

        foreach (var endDate in candidates)
        {
            var assignment = Assignment(endDate);

            if (EvaluateFutureDated(assignment))
            {
                Evaluate(assignment).ShouldBeTrue(
                    $"IsFutureDated was true for end date {endDate?.ToString() ?? "null"} but "
                        + "IsCurrent was false — the two predicates disagree on the same assignment");
            }
        }
    }

    // ------------------------------------------------------------------ HasStarted (feature 007, FR-032)

    [Fact]
    public void HasStarted_StartDateYesterday_IsTrue()
    {
        EvaluateHasStarted(Assignment(Today.AddDays(-1), endDate: null)).ShouldBeTrue();
    }

    [Fact]
    public void HasStarted_StartDateExactlyToday_IsTrue()
    {
        // Inclusive of today, matching IsCurrent's boundary. An EDJEr who starts this morning is
        // assigned today, not tomorrow — and writing `<` here is the silent defect, the mirror of the
        // `>` that IsCurrent's own remarks name.
        EvaluateHasStarted(Assignment(Today, endDate: null)).ShouldBeTrue();
    }

    [Fact]
    public void HasStarted_StartDateTomorrow_IsFalse()
    {
        EvaluateHasStarted(Assignment(Today.AddDays(1), endDate: null)).ShouldBeFalse();
    }

    [Fact]
    public void HasStarted_IsNotImpliedByIsCurrent_AFutureStartIsCurrentButNotActive()
    {
        // THE reason this predicate exists (FR-032, spec Deviation 10). IsCurrent is
        // `EndDate == null || EndDate >= today` and never looks at StartDate, so it means "not ended",
        // NOT "active". An assignment starting tomorrow with no end date satisfies IsCurrent and was
        // therefore counted on the beach tile, the rollout population, the assignment-based breakdowns
        // and all three Availability sections. Nothing caught it because all 19 seeded
        // assignments start in the past — while 006's write surface accepts a future start date.
        var startsTomorrow = Assignment(Today.AddDays(1), endDate: null);

        Evaluate(startsTomorrow).ShouldBeTrue("IsCurrent means 'not ended' and must stay that way");
        EvaluateHasStarted(startsTomorrow).ShouldBeFalse();
    }

    // ------------------------------------------------------------------ IsExpiringWithin (FR-003, BR-5)

    [Fact]
    public void IsExpiringWithin_EndDateYesterday_IsFalse()
    {
        EvaluateExpiringWithin(Sow(Today.AddDays(-1)), 90).ShouldBeFalse();
    }

    [Fact]
    public void IsExpiringWithin_EndDateToday_IsTrue()
    {
        EvaluateExpiringWithin(Sow(Today), 90).ShouldBeTrue();
    }

    [Fact]
    public void IsExpiringWithin_EndDateExactlyDay90_IsTrue()
    {
        // The inclusive boundary Session 2026-08-13 resolved: BR-5 "within 90 days" wins over AC-36's
        // "under 90 days" (spec Finding 9).
        EvaluateExpiringWithin(Sow(Today.AddDays(90)), 90).ShouldBeTrue();
    }

    [Fact]
    public void IsExpiringWithin_EndDateDay91_IsFalse()
    {
        EvaluateExpiringWithin(Sow(Today.AddDays(91)), 90).ShouldBeFalse();
    }

    // No "null end date -> false" case: unlike ClientAssignment.EndDate, Sow.SowEndDate is a REQUIRED
    // column (SowConfiguration.cs, `.IsRequired()`) and the C# property is a non-nullable DateOnly.
    // The date-predicates.md contract's boundary table listed a null case for this predicate; that was
    // written against a nullable-EndDate template and does not apply to the shipped Sow entity — an
    // `EndDate == null` comparison would not compile against a non-nullable DateOnly. Corrected here
    // rather than carried forward as an untestable requirement.

    // ------------------------------------------------------------------ IsActiveSow (issue #459)

    [Fact]
    public void IsActiveSow_StartedYesterdayEndingTomorrow_IsTrue()
    {
        EvaluateIsActiveSow(ActiveSow(Today.AddDays(-1), Today.AddDays(1))).ShouldBeTrue();
    }

    [Fact]
    public void IsActiveSow_StartDateToday_IsTrue()
    {
        // Inclusive of today at the start boundary — a SOW starting this morning is active today.
        EvaluateIsActiveSow(ActiveSow(Today, Today.AddDays(30))).ShouldBeTrue();
    }

    [Fact]
    public void IsActiveSow_EndDateToday_IsTrue()
    {
        // Inclusive of today at the end boundary — a SOW ending tonight is active for the whole day.
        EvaluateIsActiveSow(ActiveSow(Today.AddDays(-30), Today)).ShouldBeTrue();
    }

    [Fact]
    public void IsActiveSow_StartsTomorrow_IsFalse()
    {
        // Not yet begun: a future-dated SOW is not active today.
        EvaluateIsActiveSow(ActiveSow(Today.AddDays(1), Today.AddDays(30))).ShouldBeFalse();
    }

    [Fact]
    public void IsActiveSow_EndedYesterday_IsFalse()
    {
        // Already ended.
        EvaluateIsActiveSow(ActiveSow(Today.AddDays(-30), Today.AddDays(-1))).ShouldBeFalse();
    }

    // ------------------------------------------------------------------ HasNoFollowOn (FR-003, BR-5)

    [Fact]
    public void HasNoFollowOn_NoOtherSow_IsTrue()
    {
        var sows = SowSeries((Today, Today.AddDays(30)));

        EvaluateHasNoFollowOn(sows[0]).ShouldBeTrue();
    }

    [Fact]
    public void HasNoFollowOn_ASowStartingAfterThisOneEnds_IsFalse()
    {
        var sows = SowSeries((Today, Today.AddDays(30)), (Today.AddDays(31), Today.AddDays(60)));

        EvaluateHasNoFollowOn(sows[0]).ShouldBeFalse();
    }

    [Fact]
    public void HasNoFollowOn_ASowStartingBeforeThisOneEnds_IsTrue()
    {
        // A SOW that merely EXISTS on the assignment is not a follow-on if it precedes this one — it
        // may be the prior period. Existence-only would silently under-report (research D-3).
        var sows = SowSeries(
            (Today.AddDays(-60), Today.AddDays(-31)),
            (Today.AddDays(-30), Today.AddDays(30)));

        EvaluateHasNoFollowOn(sows[1]).ShouldBeTrue();
    }

    [Fact]
    public void HasNoFollowOn_TwoPriorSows_IsTrue()
    {
        var sows = SowSeries(
            (Today.AddDays(-90), Today.AddDays(-61)),
            (Today.AddDays(-60), Today.AddDays(-31)),
            (Today.AddDays(-30), Today.AddDays(30)));

        EvaluateHasNoFollowOn(sows[2]).ShouldBeTrue();
    }

    // ------------------------------------------------------------------ SowAssignmentIsOpenEndedAndActive (#457)

    [Fact]
    public void SowAssignmentIsOpenEndedAndActive_OpenEndedStartedAssignment_IsTrue()
    {
        // The population the expiring-SOWs alert exists for: no end date (unconfirmed rollout) and
        // already under way.
        var sow = SowOnAssignment(assignmentStart: Today.AddMonths(-3), assignmentEnd: null);

        EvaluateSowAssignmentOpenEndedAndActive(sow).ShouldBeTrue();
    }

    [Fact]
    public void SowAssignmentIsOpenEndedAndActive_AssignmentWithAFutureEndDate_IsFalse()
    {
        // Issue #457: a non-null end date is a PLANNED rollout — its SOW must not be flagged, even
        // though the end date is in the future and the assignment is otherwise current.
        var sow = SowOnAssignment(assignmentStart: Today.AddMonths(-3), assignmentEnd: Today.AddDays(30));

        EvaluateSowAssignmentOpenEndedAndActive(sow).ShouldBeFalse(
            "an assignment carrying an end date is a planned rollout, excluded from expiring SOWs");
    }

    [Fact]
    public void SowAssignmentIsOpenEndedAndActive_OpenEndedButNotYetStarted_IsFalse()
    {
        // "Active" per FR-032 is IsCurrent AND HasStarted. An open-ended assignment starting tomorrow
        // is current but not active, so its SOW does not yet qualify.
        var sow = SowOnAssignment(assignmentStart: Today.AddDays(1), assignmentEnd: null);

        EvaluateSowAssignmentOpenEndedAndActive(sow).ShouldBeFalse("a future-dated assignment is not active");
    }

    [Fact]
    public void SowAssignmentIsOpenEndedAndActive_OpenEndedStartingToday_IsTrue()
    {
        // The inclusive start boundary, matching HasStarted: an EDJEr starting this morning is active.
        var sow = SowOnAssignment(assignmentStart: Today, assignmentEnd: null);

        EvaluateSowAssignmentOpenEndedAndActive(sow).ShouldBeTrue("the start boundary is inclusive of today");
    }

    // ------------------------------------------------------------------ Helpers

    private bool Evaluate(ClientAssignment assignment) =>
        new[] { assignment }.AsQueryable().Any(_derivation.IsCurrent(Today));

    private bool Evaluate(Client client) =>
        new[] { client }.AsQueryable().Any(_derivation.IsActive(Today));

    private bool EvaluateHasEverBeenAssigned(Client client) =>
        new[] { client }.AsQueryable().Any(_derivation.HasEverBeenAssigned());

    private bool EvaluateFutureDated(ClientAssignment assignment) =>
        new[] { assignment }.AsQueryable().Any(_derivation.IsFutureDated(Today));

    private bool EvaluateHasStarted(ClientAssignment assignment) =>
        new[] { assignment }.AsQueryable().Any(_derivation.HasStarted(Today));

    private bool EvaluateExpiringWithin(Sow sow, int days) =>
        new[] { sow }.AsQueryable().Any(_derivation.IsExpiringWithin(Today, days));

    private bool EvaluateHasNoFollowOn(Sow sow) =>
        new[] { sow }.AsQueryable().Any(_derivation.HasNoFollowOn());

    private bool EvaluateIsActiveSow(Sow sow) =>
        new[] { sow }.AsQueryable().Any(_derivation.IsActiveSow(Today));

    private static Sow ActiveSow(DateOnly startDate, DateOnly endDate) =>
        SowSeries((startDate, endDate))[0];

    private bool EvaluateSowAssignmentOpenEndedAndActive(Sow sow) =>
        new[] { sow }.AsQueryable().Any(_derivation.SowAssignmentIsOpenEndedAndActive(Today));

    /// <summary>A single SOW whose <c>ClientAssignment</c> navigation carries a controlled start/end.</summary>
    private static Sow SowOnAssignment(DateOnly assignmentStart, DateOnly? assignmentEnd)
    {
        var assignment = new ClientAssignment { Id = 1, StartDate = assignmentStart, EndDate = assignmentEnd };
        return new Sow
        {
            ClientAssignmentId = assignment.Id,
            ClientAssignment = assignment,
            SowStartDate = assignmentStart,
            SowEndDate = assignmentStart.AddDays(60),
        };
    }

    private static ClientAssignment Assignment(DateOnly? endDate) =>
        new() { StartDate = Today.AddDays(-500), EndDate = endDate };

    private static ClientAssignment Assignment(DateOnly startDate, DateOnly? endDate) =>
        new() { StartDate = startDate, EndDate = endDate };

    private static Client Client(params ClientAssignment[] assignments) =>
        new() { ClientName = "Test Client", ClientAssignments = assignments };

    private static Sow Sow(DateOnly endDate) => SowSeries((endDate.AddDays(-30), endDate))[0];

    /// <summary>
    /// A run of SOWs on one shared assignment, fully cross-linked so <c>HasNoFollowOn</c> can walk
    /// <c>sow.ClientAssignment.Sows</c> as it would through a real EF navigation.
    /// </summary>
    private static Sow[] SowSeries(params (DateOnly Start, DateOnly End)[] periods)
    {
        var assignment = new ClientAssignment { Id = 1, StartDate = periods.Min(p => p.Start) };
        Sow[] sows =
        [
            .. periods.Select(p => new Sow
            {
                ClientAssignmentId = assignment.Id,
                ClientAssignment = assignment,
                SowStartDate = p.Start,
                SowEndDate = p.End,
            }),
        ];
        assignment.Sows = sows;

        return sows;
    }
}
