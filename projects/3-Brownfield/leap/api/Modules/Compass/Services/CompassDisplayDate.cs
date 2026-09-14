using System.Globalization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// The one way Compass renders a date a user will read.
/// </summary>
/// <remarks>
/// The server-side counterpart to <c>web/compass/src/lib/date.ts</c>. Compass composes strings a
/// person reads without a browser in the loop — a validation message a form renders verbatim, an email
/// body — and those are as much "displayed dates" as a table cell. This is not for audit values: an
/// <c>AuditEntry</c> <c>FieldChange</c> uses <c>ToString("O")</c>, a round-trip form nobody reads as
/// prose where stable machine comparison is the point. <c>CompassDisplayDateTests</c> forbids
/// <c>yyyy-MM-dd</c> specifically and leaves <c>"O"</c> alone for that reason.
/// </remarks>
public static class CompassDisplayDate
{
    /// <summary>The one display format: <c>mm/dd/yyyy</c> throughout.</summary>
    private const string DisplayFormat = "MM/dd/yyyy";

    /// <summary>
    /// Renders a date as <c>MM/dd/yyyy</c>, or the empty string when there is none.
    /// </summary>
    /// <remarks>
    /// Invariant culture, never the ambient one: a server whose locale renders <c>dd/MM/yyyy</c> would
    /// silently change what the reader sees, and "coming to an end on 09/10/2025" is ambiguous in a way
    /// nobody notices until someone acts on the wrong month. Absent renders empty rather than a
    /// placeholder, because absent is a real answer in Compass — an assignment with no end date — and
    /// choosing <c>—</c> or <c>N/A</c> belongs to whoever composes the sentence. Mirrors the SPA's
    /// <c>formatOptionalDate</c>.
    /// </remarks>
    /// <param name="date">The date to render, or <c>null</c>.</param>
    /// <returns><c>MM/dd/yyyy</c>, or an empty string.</returns>
    public static string Format(DateOnly? date) =>
        date?.ToString(DisplayFormat, CultureInfo.InvariantCulture) ?? string.Empty;
}
