using System.Xml.Linq;

using LeadingEDJE.Leap.Api.Platform.Services.OpenApi;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services.OpenApi;

public class XmlDocCommentTextTests
{
    [Fact]
    public void Extract_NullElement_ReturnsNull()
    {
        // Act
        var result = XmlDocCommentText.Extract(null);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void Extract_WhitespaceOnlyElement_ReturnsNull()
    {
        // Arrange
        var element = XElement.Parse("<summary>   \n   \n   </summary>");

        // Act
        var result = XmlDocCommentText.Extract(element);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void Extract_PlainOneLineSummary_ReturnsItUnchanged()
    {
        // Arrange
        var element = XElement.Parse("<summary>Returns one Compass employee by id.</summary>");

        // Act
        var result = XmlDocCommentText.Extract(element);

        // Assert
        result.ShouldBe("Returns one Compass employee by id.");
    }

    [Fact]
    public void Extract_MultiLineIndentedComment_CollapsesWrappedLinesToOneSpaceEach()
    {
        // Arrange -- mirrors how the C# compiler hands a wrapped, indented doc comment to Roslyn:
        // every source line keeps its leading whitespace as literal text inside the element.
        var element = XElement.Parse(
            "<summary>\n" +
            "    Read family 1 of the Compass Directory boundary (FR-004). Returns 404 when id\n" +
            "    does not match an existing employee.\n" +
            "    </summary>");

        // Act
        var result = XmlDocCommentText.Extract(element);

        // Assert -- one logical sentence, no embedded newlines or doubled spaces from the source
        // indentation.
        result.ShouldBe(
            "Read family 1 of the Compass Directory boundary (FR-004). Returns 404 when id does not "
            + "match an existing employee.");
    }

    [Fact]
    public void Extract_ParaElements_BecomeMarkdownParagraphBreaks()
    {
        // Arrange
        var element = XElement.Parse(
            "<remarks><para>First paragraph.</para><para>Second paragraph.</para></remarks>");

        // Act
        var result = XmlDocCommentText.Extract(element);

        // Assert -- CommonMark needs a BLANK line, not a single newline, to start a new paragraph.
        result.ShouldBe("First paragraph.\n\nSecond paragraph.");
    }

    [Fact]
    public void Extract_CTag_RendersAsMarkdownInlineCode()
    {
        // Arrange
        var element = XElement.Parse("<summary>The <c>TimeTracking</c> field is tier-gated.</summary>");

        // Act
        var result = XmlDocCommentText.Extract(element);

        // Assert
        result.ShouldBe("The `TimeTracking` field is tier-gated.");
    }

    /// <summary>
    /// Styling tags are banned in comments, so this renderer no longer bolds one. It degrades to
    /// the text content via the unrecognised-tag fallback, keeping the sentence intact.
    /// </summary>
    [Fact]
    public void Extract_BTag_DegradesToItsTextContent()
    {
        // Arrange
        var element = XElement.Parse("<summary><b>Never</b> null when withheld.</summary>");

        // Act
        var result = XmlDocCommentText.Extract(element);

        // Assert
        result.ShouldBe("Never null when withheld.");
    }

    [Fact]
    public void Extract_ParamrefTag_RendersAsItsNameAttribute()
    {
        // Arrange
        var element = XElement.Parse(
            "<remarks>Returns 404 when <paramref name=\"id\"/> does not match a record.</remarks>");

        // Act
        var result = XmlDocCommentText.Extract(element);

        // Assert
        result.ShouldBe("Returns 404 when id does not match a record.");
    }

    [Fact]
    public void Extract_SelfClosingSeeCrefToMethod_RendersTheMemberSimpleName()
    {
        // Arrange -- the shape every existing handler comment in this codebase actually uses:
        // self-closing, no inner text, a full doc-comment-ID cref.
        var element = XElement.Parse(
            "<summary>rather than the single-resource "
            + "<see cref=\"M:LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.CompassEmployeeEndpoints."
            + "GetById(System.Int32,LeadingEDJE.Leap.Api.Modules.Compass.Interfaces.IDirectory,"
            + "System.Threading.CancellationToken)\"/>.</summary>");

        // Act
        var result = XmlDocCommentText.Extract(element);

        // Assert -- the reference degrades to the method's own simple name, not an empty gap in the
        // sentence (a naive XElement.Value on a self-closing element would silently drop it).
        result.ShouldBe("rather than the single-resource GetById.");
    }

    [Fact]
    public void Extract_SeeCrefToType_RendersTheTypeSimpleName()
    {
        // Arrange
        var element = XElement.Parse(
            "<remarks>see <see cref=\"T:LeadingEDJE.Leap.Api.Modules.Compass.Dtos.CompassClientDto\"/> "
            + "for details.</remarks>");

        // Act
        var result = XmlDocCommentText.Extract(element);

        // Assert
        result.ShouldBe("see CompassClientDto for details.");
    }

    [Fact]
    public void Extract_SeeCrefWithInnerText_PrefersTheInnerTextOverTheCref()
    {
        // Arrange
        var element = XElement.Parse(
            "<summary>see <see cref=\"T:Some.Namespace.Thing\">the other type</see> instead.</summary>");

        // Act
        var result = XmlDocCommentText.Extract(element);

        // Assert
        result.ShouldBe("see the other type instead.");
    }

    [Fact]
    public void Extract_XmlComment_ContributesNoText()
    {
        // Arrange -- an XML comment node is neither XText nor XElement; RenderNode's default arm
        // must render it as nothing rather than throw or leak the comment's own text.
        var element = XElement.Parse("<summary>Before<!-- an aside -->After</summary>");

        // Act
        var result = XmlDocCommentText.Extract(element);

        // Assert
        result.ShouldBe("BeforeAfter");
    }

    [Fact]
    public void Extract_UnrecognisedTag_FallsBackToItsTextContent()
    {
        // Arrange -- an unrecognised tag must degrade to plain text, never throw.
        var element = XElement.Parse("<summary>Some <em>emphasised</em> word.</summary>");

        // Act
        var result = XmlDocCommentText.Extract(element);

        // Assert
        result.ShouldBe("Some emphasised word.");
    }
}
