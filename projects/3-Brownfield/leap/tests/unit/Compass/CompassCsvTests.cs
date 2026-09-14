using System.Text;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The CSV writer behind the Compass report exports (issue #337).
/// </summary>
/// <remarks>
/// <para>
/// Why Compass writes its own rather than reusing the timesheet module's.
/// <c>CsvFallbackReportService.EscapeCsvValue</c> already does RFC 4180 quoting, but it is a private
/// member of a timesheet service, and a Compass service may not reach into the timesheet module — the
/// dependency direction a module boundary implies. Two
/// occurrences is also below the Rule of Three, so extracting a shared platform helper now would be
/// speculative. It additionally does NOT neutralise formula injection, which is the half that matters
/// most here.
/// </para>
/// <para>
/// Formula injection is a real exposure on this data, not a theoretical one. Client names and
/// EDJEr display names are operator-editable through Compass's own admin screens, and a cell beginning
/// <c>=</c>, <c>+</c>, <c>@</c> or <c>-</c> is evaluated as a formula when the file is opened in Excel
/// or Sheets. The repository already treats the analogous log-forging case as worth a dedicated helper
/// (<c>LogSanitizer</c>), so the same standard applies to data leaving the system as a file.
/// </para>
/// </remarks>
public class CompassCsvTests
{
    // ------------------------------------------------------------------ RFC 4180

    [Fact]
    public void Write_JoinsFieldsWithCommas_AndTerminatesRowsWithCrLf()
    {
        // Arrange / Act
        var csv = Text(CompassCsv.Write(["A", "B"], [["1", "2"], ["3", "4"]]));

        // Assert — CRLF, not LF: RFC 4180's line terminator and what Excel expects.
        csv.ShouldBe("A,B\r\n1,2\r\n3,4\r\n");
    }

    [Fact]
    public void Write_QuotesFieldsContainingCommasQuotesOrNewlines()
    {
        // Arrange / Act
        var csv = Text(CompassCsv.Write(
            ["Name"],
            [["Acme, Inc."], ["He said \"hi\""], ["line1\nline2"]]));

        // Assert — an embedded quote doubles; the whole field is then wrapped.
        var rows = csv.Split("\r\n");
        rows[1].ShouldBe("\"Acme, Inc.\"");
        rows[2].ShouldBe("\"He said \"\"hi\"\"\"");
        csv.ShouldContain("\"line1\nline2\"");
    }

    [Fact]
    public void Write_LeavesOrdinaryFieldsUnquoted()
    {
        // A writer that quotes everything is technically valid CSV and unreadable as a diff, which is
        // how a wrong column ordering survives review.
        Text(CompassCsv.Write(["Name"], [["Odessa Ferrante"]]))
            .ShouldBe("Name\r\nOdessa Ferrante\r\n");
    }

    [Fact]
    public void Write_RendersNullAsAnEmptyField()
    {
        // Coach is nullable on every report row and a coachless EDJEr must stay in the file (FR-013).
        Text(CompassCsv.Write(["EDJEr", "Coach"], [["Amara Dunmore", null]]))
            .ShouldBe("EDJEr,Coach\r\nAmara Dunmore,\r\n");
    }

    [Fact]
    public void Write_WithNoRows_EmitsTheHeaderOnly()
    {
        // An empty export is a valid answer; the file still documents its columns.
        Text(CompassCsv.Write(["EDJEr", "Client"], [])).ShouldBe("EDJEr,Client\r\n");
    }

    // ------------------------------------------------------------------ formula injection

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+1+1")]
    [InlineData("@SUM(A1)")]
    [InlineData("=cmd|'/c calc'!A1")]
    public void Write_NeutralisesFieldsThatWouldEvaluateAsAFormula(string dangerous)
    {
        // Arrange / Act
        var csv = Text(CompassCsv.Write(["Client"], [[dangerous]]));

        // Assert — prefixed with an apostrophe, which spreadsheets read as "this is text". The value
        // is quoted too, since the apostrophe form still needs protecting from any embedded comma.
        csv.ShouldBe($"Client\r\n\"'{dangerous}\"\r\n");
    }

    [Theory]
    [InlineData("\tLeading tab")]
    [InlineData("\rLeading CR")]
    public void Write_NeutralisesLeadingControlCharacters(string dangerous)
    {
        // Both are documented lead-ins for the same attack, and both are invisible in review.
        Text(CompassCsv.Write(["Client"], [[dangerous]]))
            .ShouldContain($"\"'{dangerous}\"");
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("-1238")]
    [InlineData("1238")]
    [InlineData("-0.5")]
    public void Write_LeavesNegativeAndPlainNumbersIntact(string number)
    {
        // The interesting half of the injection rule. A blanket "prefix anything starting with -"
        // would corrupt every negative number in the file -- and `# Days Until SOW Expiration` can
        // legitimately be negative for a SOW already past its end date. Numbers are data; only a
        // NON-numeric field opening with a formula lead-in gets neutralised.
        Text(CompassCsv.Write(["Days"], [[number]])).ShouldBe($"Days\r\n{number}\r\n");
    }

    [Fact]
    public void Write_DoesNotNeutraliseAFormulaCharacterMidField()
    {
        // Only the FIRST character starts a formula. Rewriting "Smith-Jones" or "A+B Consulting"
        // would corrupt real client names for no gain.
        Text(CompassCsv.Write(["Client"], [["A+B Consulting"], ["Smith-Jones"]]))
            .ShouldBe("Client\r\nA+B Consulting\r\nSmith-Jones\r\n");
    }

    // ------------------------------------------------------------------ encoding

    [Fact]
    public void Write_PrefixesAUtf8Bom_SoExcelDecodesNonAsciiNames()
    {
        // Arrange / Act
        var bytes = CompassCsv.Write(["Name"], [["Zoë Ferrante"]]);

        // Assert
        bytes[..3].ShouldBe([(byte)0xEF, (byte)0xBB, (byte)0xBF]);
        new UTF8Encoding(false).GetString(bytes[3..]).ShouldContain("Zoë Ferrante");
    }

    [Fact]
    public void Write_RejectsARowWhoseFieldCountDoesNotMatchTheHeader()
    {
        // A silent short row shifts every later column and is invisible in a spreadsheet. Failing
        // loudly at the writer is the only place this is cheap to catch.
        Should.Throw<ArgumentException>(() =>
            CompassCsv.Write(["A", "B"], [["only one"]]));
    }

    private static string Text(byte[] csv) => new UTF8Encoding(false).GetString(csv[3..]);
}
