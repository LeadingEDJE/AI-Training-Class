using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The calendar-day boundary, asserted on the composition — feature 004 US4, T091 and T092.
/// </summary>
/// <remarks>
/// <para>
/// <c>CompassBusinessDateTests</c> proves the clock converts to <c>America/New_York</c>, and
/// <c>ClientStatusDerivationTests</c> proves the comparison is inclusive of a given date. Neither
/// proves the property the contract actually states, which is about a wall-clock instant: "active
/// at 00:01 and 23:59 on the end date, inactive on the following date with no data change".
/// That property spans both components, so a defect in how they are wired together — a consumer converting to UTC
/// before handing a date over, say — is invisible to both files.
/// </para>
/// <para>
/// The failure this guards against is live in this repository.
/// <c>api/Modules/Ooto/Services/OotoService.cs</c> takes <c>GetUtcNow().UtcDateTime.Date</c> for its
/// own "today", which during Eastern Daylight Time is tomorrow's date from 20:00 onward. Wired that
/// way, a client whose assignment ends today stops reading Active for the last four or five hours
/// of its final working day — the exact behaviour the owner's rule forbids, and one nobody
/// notices because tests do not run at 8 p.m. (Since issue #274 it would read <c>Former</c> rather
/// than <c>Inactive</c>, the assignment having existed; the defect is identical either way.)
/// </para>
/// <para>
/// Every instant here is injected, never taken from the wall clock: a test pinned to an
/// absolute date passes the day it is written and fails later (SC-005).
/// </para>
/// </remarks>
public class ClientStatusCalendarDayBoundaryTests
{
    /// <summary>The end date under test. A June date, so Eastern is at UTC-4 (daylight time).</summary>
    private static readonly DateOnly EndDate = new(2026, 6, 15);

    private readonly ClientStatusDerivation _derivation = new();

    // ------------------------------------------------------------------ Time-of-day invariance

    [Fact]
    public void EndsToday_JustAfterMidnightEastern_IsActive()
    {
        // 00:01 EDT on the end date is 04:01 UTC the same day.
        StatusAt(Eastern(2026, 6, 15, 0, 1)).ShouldBe(ClientStatus.Active);
    }

    [Fact]
    public void EndsToday_OneMinuteBeforeMidnightEastern_IsStillActive()
    {
        // 23:59 EDT on the end date is 03:59 UTC on the FOLLOWING day. This is the assertion that
        // fails the moment anything in the chain takes a UTC date.
        StatusAt(Eastern(2026, 6, 15, 23, 59)).ShouldBe(ClientStatus.Active);
    }

    [Fact]
    public void EndsToday_AtEightInTheEveningEastern_IsStillActive()
    {
        // T092, stated as the contract states it: 20:00 EDT is 00:00 UTC tomorrow. Under a UTC date
        // this client stops reading Active while its final working day still has four hours left.
        var instant = Eastern(2026, 6, 15, 20, 0);

        instant.UtcDateTime.Date.ShouldBe(
            new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Unspecified),
            "if this drifts the test has stopped exercising the UTC-date failure mode");

        StatusAt(instant).ShouldBe(ClientStatus.Active);
    }

    [Fact]
    public void EndsToday_AcrossEveryHourOfTheDayEastern_IsActiveAtAllOfThem()
    {
        // The property rather than three spot checks: status must not depend on the time of day at
        // all. A boundary written against a timestamp instead of a date fails somewhere in here.
        for (var hour = 0; hour < 24; hour++)
        {
            StatusAt(Eastern(2026, 6, 15, hour, 30))
                .ShouldBe(ClientStatus.Active, $"{hour:00}:30 Eastern on the end date");
        }
    }

    // ------------------------------------------------------------------ The transition no write causes

    [Fact]
    public void TheFollowingDay_IsFormer_WithNoDataChange()
    {
        // US4 scenario 4a. The SAME client object is evaluated at both instants — nothing is written,
        // nothing is re-read, and the status changes. That is the whole point of deriving it.
        //
        // Since issue #274 the far side of the boundary is FORMER, not Inactive: this client held an
        // assignment, so it is one we have stopped working with rather than one we never engaged. The
        // transition itself is unchanged — only the word for the ended state.
        var client = ClientEndingOn(EndDate);

        StatusAt(Eastern(2026, 6, 15, 23, 59), client).ShouldBe(ClientStatus.Active);
        StatusAt(Eastern(2026, 6, 16, 0, 1), client).ShouldBe(ClientStatus.Former);
    }

    // ------------------------------------------------------------------ DST

    [Fact]
    public void AcrossTheSpringForward_TheBoundaryTracksTheZone()
    {
        // 8 March 2026 loses an hour at 02:00 local and the zone becomes EDT (UTC-4), so 23:59 local
        // that day is 03:59 UTC on the 9th — one hour earlier in UTC than the same local time on the
        // 7th would have been. A hard-coded offset gets one side of this transition wrong.
        var client = ClientEndingOn(new DateOnly(2026, 3, 8));

        StatusAt(new DateTimeOffset(2026, 3, 9, 3, 59, 0, TimeSpan.Zero), client)
            .ShouldBe(ClientStatus.Active);
        StatusAt(new DateTimeOffset(2026, 3, 9, 4, 1, 0, TimeSpan.Zero), client)
            .ShouldBe(ClientStatus.Former);
    }

    [Fact]
    public void AcrossTheFallBack_TheBoundaryTracksTheZone()
    {
        // 1 November 2026 gains an hour at 02:00 local. 23:59 local is 04:59 UTC on the 2nd (EST,
        // UTC-5), where the same local time in June is 03:59 UTC.
        var client = ClientEndingOn(new DateOnly(2026, 11, 1));

        StatusAt(new DateTimeOffset(2026, 11, 2, 4, 59, 0, TimeSpan.Zero), client)
            .ShouldBe(ClientStatus.Active);
        StatusAt(new DateTimeOffset(2026, 11, 2, 5, 1, 0, TimeSpan.Zero), client)
            .ShouldBe(ClientStatus.Former);
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>The status of a client whose only assignment ends on <see cref="EndDate"/>.</summary>
    private ClientStatus StatusAt(DateTimeOffset instant) => StatusAt(instant, ClientEndingOn(EndDate));

    /// <summary>
    /// Derives status the way a consumer must: the business date from the one place that reads the
    /// clock, then the one derivation.
    /// </summary>
    private ClientStatus StatusAt(DateTimeOffset instant, Client client)
    {
        var today = new CompassBusinessDate(new FixedClock(instant)).Today();

        return _derivation.Of(client, today);
    }

    private static DateTimeOffset Eastern(int year, int month, int day, int hour, int minute)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static Client ClientEndingOn(DateOnly endDate) =>
        new()
        {
            ClientName = "Ends On A Boundary",
            ClientAssignments =
            [
                new ClientAssignment { StartDate = endDate.AddDays(-100), EndDate = endDate },
            ],
        };

    /// <summary>A clock pinned to one instant.</summary>
    /// <remarks>
    /// A third copy of this five-line double now exists (<c>CompassBusinessDateTests</c> and
    /// <c>OotoWeeklySummaryJobTests</c> hold the others), so the Rule of Three has fired and the
    /// extraction is owed. It is deliberately NOT done in this commit: the constitution's Principle VI
    /// keeps refactoring and behaviour change out of the same commit.
    /// </remarks>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
