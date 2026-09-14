using System.Globalization;
using System.Text;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Renders a Compass report as an RFC 4180 CSV payload, Excel-safe.
/// </summary>
/// <remarks>
/// Compass-local on purpose: the timesheet module's RFC 4180 quoting is private to
/// <c>CsvFallbackReportService</c>, a Compass service may not depend on the timesheet module,
/// two occurrences is below the Rule of Three, and that one
/// does not neutralise formula injection, which is the half this data needs most. It returns bytes
/// rather than a string because the UTF-8 BOM is part of the contract — without it Excel decodes the
/// file in the local ANSI code page and mangles any non-ASCII name, and a string would let a caller
/// lose the BOM by re-encoding.
/// </remarks>
public static class CompassCsv
{
    /// <summary>
    /// The characters that make a spreadsheet treat a cell as a formula when they lead it.
    /// </summary>
    /// <remarks>
    /// Tab and carriage return are included because both are documented lead-ins for the same attack
    /// and both are invisible when reading a diff.
    /// </remarks>
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

        // Encoding with a BOM-emitting UTF8Encoding only writes the preamble via a StreamWriter, so
        // the bytes are composed explicitly here.
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

        // CRLF is RFC 4180's terminator and what Excel expects.
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
            // An apostrophe is the spreadsheet convention for "treat this as text". Always quoted as
            // well, so an embedded comma cannot split the neutralised value across two cells.
            return Quote($"'{value}");
        }

        return NeedsQuoting(value) ? Quote(value) : value;
    }

    /// <summary>
    /// True when the field leads with a formula character AND is not simply a number.
    /// </summary>
    /// <remarks>
    /// The numeric exemption is the point, not a loophole. A blanket rule on a leading
    /// <c>-</c> would rewrite every negative number in the file, and <c># Days Until SOW
    /// Expiration</c> is legitimately negative for a SOW already past its end date — so the
    /// "protection" would corrupt real data on a real column. Invariant culture, because the
    /// rendered values are invariant.
    /// </remarks>
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
