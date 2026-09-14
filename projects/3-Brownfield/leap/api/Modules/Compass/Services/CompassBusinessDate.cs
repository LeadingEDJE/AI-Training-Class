using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>"Today" as a date in the business timezone, from the injected <see cref="TimeProvider"/>.</summary>
/// <remarks>
/// The timezone is part of the contract, not an implementation detail. Under UTC a US business
/// day ends at 8 p.m. Eastern, so an assignment ending "today" would flip to inactive for the last
/// four or five hours of its final working day. Both behaviours already exist here —
/// <c>OotoService</c> uses UTC, <c>OotoWeekMath</c> converts first — and Compass follows the latter.
/// </remarks>
public class CompassBusinessDate(TimeProvider timeProvider) : ICompassBusinessDate
{
    private static readonly TimeZoneInfo BusinessZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <inheritdoc />
    public DateOnly Today() =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(timeProvider.GetUtcNow().UtcDateTime, BusinessZone));
}
