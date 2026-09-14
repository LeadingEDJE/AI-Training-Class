using System.Globalization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>Renders a day count in the mockup's duration format (feature 007, FR-030, RPT-4).</summary>
/// <remarks>
/// Pure and static so it can be asserted directly. Months are whole months of 30 days after whole
/// years of 365, capped at 11 — a display convention, not a calendar calculation: the
/// authoritative value is <c>TotalDays</c>, which is what the report sorts on, and the mockup shows the
/// day count in parentheses precisely so the rounded part is never the number a reader relies on. The
/// cap exists because those two divisors do not agree: without it the remainder can yield 12 months and
/// claim a year the year count did not.
/// </remarks>
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

        // Capped at 11, and the cap is load-bearing: the remainder after whole years is 0..364 and
        // 364 / 30 is 12, so the uncapped form rendered "12 mos (360 days)", announcing a year the year
        // count did not claim. Capping rather than rolling into a year is deliberate — 360 days is not
        // a year. The two divisors are inconsistent on purpose (365 and 30), so the months figure is a
        // rounding of the remainder, which is why the day count sits beside it and is what sorts.
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

        // Under a month there is no years/months part to lead with, so the day count stands alone
        // rather than being rendered as an empty prefix plus parentheses.
        return parts.Count == 0
            ? $"{count} {unit}"
            : $"{string.Join(' ', parts)} ({count} {unit})";
    }
}
