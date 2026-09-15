using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>"Today" as a UTC date, from the injected <see cref="TimeProvider"/>.</summary>
public class CompassBusinessDate(TimeProvider timeProvider) : ICompassBusinessDate
{
    private static readonly TimeZoneInfo BusinessZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <inheritdoc />
    public DateOnly Today() =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(timeProvider.GetUtcNow().UtcDateTime, BusinessZone));
}
