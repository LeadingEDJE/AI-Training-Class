using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LeadingEDJE.Leap.Api.Platform.Services.OpenApi;

/// <summary>
/// Converts one <c>&lt;summary&gt;</c>/<c>&lt;remarks&gt;</c> XML documentation element into plain,
/// lightly-Markdown text suitable for an OpenAPI operation's <c>summary</c>/<c>description</c>.
/// </summary>
/// <remarks>
/// The framework's own converter is not reusable here: its compile-time cache omits every
/// <c>private</c> member, so this reads the compiled documentation FILE at runtime instead. See
/// <see cref="PrivateHandlerXmlDocOperationTransformer"/>. Deliberately not a general-purpose
/// renderer -- it handles only <c>&lt;para&gt;</c>, <c>&lt;c&gt;</c>/<c>&lt;code&gt;</c>,
/// <c>&lt;see cref&gt;</c>/<c>&lt;seealso cref&gt;</c> and <c>&lt;paramref&gt;</c>/
/// <c>&lt;typeparamref&gt;</c>. Any other tag degrades to its text content rather than throwing.
/// </remarks>
internal static class XmlDocCommentText
{
    private static readonly Regex RunsOfHorizontalWhitespace = new(@"[ \t]+", RegexOptions.Compiled);

    /// <summary>
    /// Renders the element's content as plain/lightly-Markdown text, or <c>null</c> if it has none.
    /// </summary>
    public static string? Extract(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var rendered = RenderChildren(element);
        var collapsed = CollapseWhitespace(rendered);
        return string.IsNullOrWhiteSpace(collapsed) ? null : collapsed;
    }

    private static string RenderChildren(XElement element) =>
        string.Concat(element.Nodes().Select(RenderNode));

    private static string RenderNode(XNode node) =>
        node switch
        {
            XText text => text.Value,
            XElement element => RenderElement(element),
            _ => string.Empty,
        };

    private static string RenderElement(XElement element)
    {
        var inner = RenderChildren(element);
        return element.Name.LocalName switch
        {
            "para" => "\n\n" + inner.Trim(),
            "c" or "code" => "`" + inner.Trim() + "`",
            "see" or "seealso" => RenderReference(element, inner),
            "paramref" or "typeparamref" => (string?)element.Attribute("name") ?? inner,
            _ => inner,
        };
    }

    /// <summary>
    /// Renders a <c>&lt;see cref="..."/&gt;</c>-style reference. Most uses here are self-closing, so the
    /// fallback -- the simple member name recovered from the <c>cref</c> -- is the common case.
    /// </summary>
    private static string RenderReference(XElement element, string inner)
    {
        if (!string.IsNullOrWhiteSpace(inner))
        {
            return inner.Trim();
        }

        var cref = (string?)element.Attribute("cref");
        if (string.IsNullOrEmpty(cref))
        {
            return string.Empty;
        }

        // Doc-comment IDs look like "M:Namespace.Type.Member(ParamType,...)" or "T:Namespace.Type".
        // Strip the one-letter-plus-colon kind prefix, then the parameter list, then keep only the
        // last dotted segment -- the member/type's own simple name, which is what a reader wants in
        // running prose (e.g. "GetById", not the fully-qualified "Namespace.SomeType.
        // GetById(System.Int32,...)" the compiler wrote into the cref attribute).
        var withoutKindPrefix = cref.Length > 1 && cref[1] == ':' ? cref[2..] : cref;
        var parenIndex = withoutKindPrefix.IndexOf('(');
        var withoutParameters = parenIndex >= 0 ? withoutKindPrefix[..parenIndex] : withoutKindPrefix;
        var lastDot = withoutParameters.LastIndexOf('.');
        return lastDot >= 0 ? withoutParameters[(lastDot + 1)..] : withoutParameters;
    }

    /// <summary>
    /// Collapses the source indentation the compiler preserves verbatim into single spaces, one output
    /// line per input line, keeping the <c>&lt;para&gt;</c> breaks <see cref="RenderElement"/> inserted.
    /// </summary>
    private static string CollapseWhitespace(string text)
    {
        var lines = text.Split('\n')
            .Select(line => RunsOfHorizontalWhitespace.Replace(line, " ").Trim());

        // A blank input line only ever means "paragraph break" here (either a literal blank line in
        // the source, or the "\n\n" RenderElement inserted for <para>). Any run of one-or-more blank
        // lines collapses to exactly one Markdown paragraph break ("\n\n") in the output, never more.
        var sb = new StringBuilder();
        var pendingParagraphBreak = false;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                if (sb.Length > 0)
                {
                    pendingParagraphBreak = true;
                }

                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(pendingParagraphBreak ? "\n\n" : " ");
            }

            pendingParagraphBreak = false;
            sb.Append(line);
        }

        return sb.ToString().Trim();
    }
}
