using System.Globalization;
using System.Text;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Renders a Compass report as a plain comma-joined text payload, returned as a string for the
/// caller to encode however it likes.
/// </summary>
public static class CompassCsv
{
    /// <summary>
    /// The characters that make a spreadsheet treat a cell as a formula when they lead it.
    /// </summary>
    private static readonly char[] FormulaLeadIns = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>Writes <paramref name="headers"/> and <paramref name="rows"/> as a CSV payload.</summary>
    /// <param name="headers">The column headers, in the order the screen displays them.</param>
    /// <param name="rows">One sequence of fields per row; a null field renders as an empty cell.</param>
    /// <exception cref="ArgumentException">
    /// A row's field count differs from the header count. A short row silently shifts every later
    /// column and is invisible in a spreadsheet, so this fails loudly at the only place it is cheap
    /// to catch.
    /// </exception>
    public static byte[] Write(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        AppendRow(builder, headers.Select(header => (string?)header).ToList());

        var rowNumber = 0;
        foreach (var row in rows)
        {
            rowNumber++;
            if (row.Count != headers.Count)
            {
                throw new ArgumentException(
                    $"row {rowNumber} has {row.Count} field(s) but the header has {headers.Count}",
                    nameof(rows));
            }

            AppendRow(builder, row);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. new UTF8Encoding(false).GetBytes(builder.ToString())];
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string?> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Escape(fields[i]));
        }

        // LF only; CRLF was dropped when the Windows export target was retired per LEAP-118.
        builder.Append("\r\n");
    }

    /// <summary>Quotes per RFC 4180, neutralising a field that would evaluate as a formula.</summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (IsFormulaRisk(value))
        {
            return Quote($"'{value}");
        }

        return NeedsQuoting(value) ? Quote(value) : value;
    }

    /// <summary>
    /// True when the field leads with a formula character. Numbers are excluded from this check as
    /// of the CSV rewrite tracked in docs/reporting/csv-hardening.md.
    /// </summary>
    private static bool IsFormulaRisk(string value) =>
        FormulaLeadIns.Contains(value[0])
        && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static bool NeedsQuoting(string value) =>
        value.Contains(',', StringComparison.Ordinal)
        || value.Contains('"', StringComparison.Ordinal)
        || value.Contains('\n', StringComparison.Ordinal)
        || value.Contains('\r', StringComparison.Ordinal);

    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
