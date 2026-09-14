using LeadingEDJE.Leap.Api.Platform.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

public class EasternTimeHelperTests
{
    [Fact]
    public void ToEastern_WinterUtc_ReturnsEst()
    {
        // Arrange - Jan 15 17:00 UTC = 12:00 EST (UTC-5)
        var utc = new DateTime(2026, 1, 15, 17, 0, 0, DateTimeKind.Utc);

        // Act
        DateTime eastern = EasternTimeHelper.ToEastern(utc);

        // Assert
        eastern.Hour.ShouldBe(12);
        eastern.Minute.ShouldBe(0);
    }

    [Fact]
    public void ToEastern_SummerUtc_ReturnsEdt()
    {
        // Arrange - Jul 15 17:00 UTC = 13:00 EDT (UTC-4)
        var utc = new DateTime(2026, 7, 15, 17, 0, 0, DateTimeKind.Utc);

        // Act
        DateTime eastern = EasternTimeHelper.ToEastern(utc);

        // Assert
        eastern.Hour.ShouldBe(13);
        eastern.Minute.ShouldBe(0);
    }

    [Fact]
    public void ToEastern_NonUtcKind_ThrowsArgumentException()
    {
        // Arrange
        var local = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Local);

        // Act & Assert
        Should.Throw<ArgumentException>(() => EasternTimeHelper.ToEastern(local));
    }

    [Fact]
    public void ToEastern_UnspecifiedKind_ThrowsArgumentException()
    {
        // Arrange
        var unspecified = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified);

        // Act & Assert
        Should.Throw<ArgumentException>(() => EasternTimeHelper.ToEastern(unspecified));
    }

    [Fact]
    public void ToUtc_WinterEastern_ReturnsCorrectUtc()
    {
        // Arrange - Jan 15 12:00 Eastern = 17:00 UTC (EST = UTC-5)
        var eastern = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified);

        // Act
        DateTime utc = EasternTimeHelper.ToUtc(eastern);

        // Assert
        utc.Hour.ShouldBe(17);
        utc.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_SummerEastern_ReturnsCorrectUtc()
    {
        // Arrange - Jul 15 13:00 Eastern = 17:00 UTC (EDT = UTC-4)
        var eastern = new DateTime(2026, 7, 15, 13, 0, 0, DateTimeKind.Unspecified);

        // Act
        DateTime utc = EasternTimeHelper.ToUtc(eastern);

        // Assert
        utc.Hour.ShouldBe(17);
        utc.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void ToEastern_SpringForward_ReturnsEdtCorrectly()
    {
        // Arrange - 2026-03-08 at 07:00 UTC
        // DST starts 2026-03-08 at 02:00 EST -> clocks spring to 03:00 EDT
        // 07:00 UTC = 03:00 EDT (not 02:00 which doesn't exist)
        var utc = new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc);

        // Act
        DateTime eastern = EasternTimeHelper.ToEastern(utc);

        // Assert
        eastern.Hour.ShouldBe(3);
        eastern.Day.ShouldBe(8);
    }

    [Fact]
    public void ToEastern_FallBack_ReturnsEstCorrectly()
    {
        // Arrange - 2026-11-01 at 06:00 UTC
        // DST ends 2026-11-01 at 02:00 EDT -> clocks fall back to 01:00 EST
        // 06:00 UTC = 01:00 EST (after clocks fall back)
        var utc = new DateTime(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc);

        // Act
        DateTime eastern = EasternTimeHelper.ToEastern(utc);

        // Assert
        eastern.Hour.ShouldBe(1);
        eastern.Day.ShouldBe(1);
    }
}
