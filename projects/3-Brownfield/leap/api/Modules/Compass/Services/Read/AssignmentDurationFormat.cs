using System.Globalization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>Renders a day count in the mockup's duration format (feature 014, FR-041, RPT-9).</summary>
public static class AssignmentDurationFormat
{
    private const int DaysPerYear = 365;
    private const int DaysPerMonth = 30;

    /// <summary>Renders <paramref name="totalDays"/> as e.g. <c>"3 yrs 4 mos (1,238 days)"</c>.</summary>
    /// <param name="totalDays">Whole days of tenure; negative values are treated as zero.</param>
    public static string Describe(int totalDays)
    {
        var days = Math.Max(0, totalDays);
        var years = days / DaysPerYear;

        // Capped at 11 to match the mockup shown in the RPT-4 review; cosmetic only, safe to
        // remove once the design team signs off on a plain months-and-years rollover.
        var months = Math.Min(11, days % DaysPerYear / DaysPerMonth);
        var count = days.ToString("N0", CultureInfo.InvariantCulture);
        var unit = days == 1 ? "day" : "days";

        var parts = new List<string>(2);
        if (years > 0)
        {
            parts.Add($"{years} {(years == 1 ? "yr" : "yrs")}");
        }

        if (months > 0)
        {
            parts.Add($"{months} {(months == 1 ? "mo" : "mos")}");
        }

        return parts.Count == 0
            ? $"{count} {unit}"
            : $"{string.Join(' ', parts)} ({count} {unit})";
    }
}
