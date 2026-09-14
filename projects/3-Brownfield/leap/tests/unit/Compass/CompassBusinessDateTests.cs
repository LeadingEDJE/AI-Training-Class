using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The single source of "today" for every Compass derivation.
/// </summary>
/// <remarks>
/// <para>
/// The timezone is part of the contract, not an implementation detail. Under UTC a US
/// business day ends at 8 p.m. Eastern, so a client or assignment ending "today" would flip to
/// inactive for the last four or five hours of its final working day — violating the owner's rule
/// directly.
/// </para>
/// <para>
/// Both behaviours already exist in this repository: <c>OotoService</c> takes
/// <c>GetUtcNow().UtcDateTime.Date</c>, while <c>OotoWeekMath</c> converts to
/// <c>America/New_York</c> first. Compass follows <c>OotoWeekMath</c>.
/// </para>
/// </remarks>
public class CompassBusinessDateTests
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public void Today_LateEveningEastern_IsStillTheEasternDate()
    {
        // Arrange — 23:30 Eastern on 14 March 2026 is already 03:30 UTC on the 15th.
        var provider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 15, 3, 30, 0, TimeSpan.Zero));
        var businessDate = new CompassBusinessDate(provider);

        // Act
        var today = businessDate.Today();

        // Assert — a UTC-based implementation returns the 15th here, which is the whole defect.
        today.ShouldBe(new DateOnly(2026, 3, 14));
    }

    [Fact]
    public void Today_MiddayEastern_IsThatDate()
    {
        // Arrange
        var provider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 14, 16, 0, 0, TimeSpan.Zero));
        var businessDate = new CompassBusinessDate(provider);

        // Act / Assert
        businessDate.Today().ShouldBe(new DateOnly(2026, 3, 14));
    }

    [Fact]
    public void Today_AcrossTheSpringDstTransition_TracksTheZoneNotAFixedOffset()
    {
        // Arrange — US DST began 8 March 2026. Eastern is UTC-5 before and UTC-4 after, so an
        // implementation hard-coding either offset gets one of these two wrong.
        var beforeDst = new FakeTimeProvider(new DateTimeOffset(2026, 3, 8, 4, 30, 0, TimeSpan.Zero));
        var afterDst = new FakeTimeProvider(new DateTimeOffset(2026, 3, 9, 3, 30, 0, TimeSpan.Zero));

        // Act / Assert — 23:30 EST on the 7th, then 23:30 EDT on the 8th.
        new CompassBusinessDate(beforeDst).Today().ShouldBe(new DateOnly(2026, 3, 7));
        new CompassBusinessDate(afterDst).Today().ShouldBe(new DateOnly(2026, 3, 8));
    }

    [Fact]
    public void Today_AcrossTheAutumnDstTransition_TracksTheZone()
    {
        // Arrange — US DST ended 1 November 2026.
        var beforeEnd = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 3, 30, 0, TimeSpan.Zero));
        var afterEnd = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 4, 30, 0, TimeSpan.Zero));

        // Act / Assert
        new CompassBusinessDate(beforeEnd).Today().ShouldBe(new DateOnly(2026, 10, 31));
        new CompassBusinessDate(afterEnd).Today().ShouldBe(new DateOnly(2026, 11, 1));
    }

    [Fact]
    public void Today_UsesTheInjectedProvider_NeverTheWallClock()
    {
        // Arrange — a date far from now. If the implementation reads the ambient clock this fails,
        // and a test that merely compared against DateTime.UtcNow would pass the day it was written
        // and fail later.
        var provider = new FakeTimeProvider(new DateTimeOffset(2019, 7, 4, 17, 0, 0, TimeSpan.Zero));

        // Act / Assert
        new CompassBusinessDate(provider).Today().ShouldBe(new DateOnly(2019, 7, 4));
    }

    /// <summary>
    /// A clock pinned to a fixed instant.
    /// </summary>
    /// <remarks>
    /// Declared locally rather than shared, mirroring the identical private double in
    /// <c>OotoWeeklySummaryJobTests</c>. That is two instances, not three — the constitution's
    /// Rule of Three says extract when duplication has actually emerged, and a shared fixture for
    /// two five-line classes would be the abstraction arriving early. The repository carries no
    /// <c>Microsoft.Extensions.TimeProvider.Testing</c> reference, and adding a package to replace
    /// five lines is the heavier option.
    /// </remarks>
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void Today_AgreesWithAnIndependentConversion_AcrossAFullYear()
    {
        // A property check rather than a spot check: for one instant per day across a year, the
        // result must equal an independently written Eastern conversion.
        var instant = new DateTimeOffset(2026, 1, 1, 5, 30, 0, TimeSpan.Zero);

        for (var day = 0; day < 365; day++)
        {
            var provider = new FakeTimeProvider(instant.AddDays(day));
            var expected = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(instant.AddDays(day).UtcDateTime, Eastern));

            new CompassBusinessDate(provider).Today().ShouldBe(expected);
        }
    }
}
