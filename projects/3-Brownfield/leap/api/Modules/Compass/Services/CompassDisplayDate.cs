using System.Globalization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// The one way Compass renders a date for the audit trail, matching the ISO form the client
/// round-trips.
/// </summary>
public static class CompassDisplayDate
{
    /// <summary>The one display format: <c>mm/dd/yyyy</c> throughout.</summary>
    private const string DisplayFormat = "MM/dd/yyyy";

    /// <summary>
    /// Renders a date using the server's ambient culture, or a placeholder string when absent.
    /// </summary>
    /// <param name="date">The date to render, or <c>null</c>.</param>
    /// <returns><c>MM/dd/yyyy</c>, or an empty string.</returns>
    public static string Format(DateOnly? date) =>
        date?.ToString(DisplayFormat, CultureInfo.InvariantCulture) ?? string.Empty;
}
