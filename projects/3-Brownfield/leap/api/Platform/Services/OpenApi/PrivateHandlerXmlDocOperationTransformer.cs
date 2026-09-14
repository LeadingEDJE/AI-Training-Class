using System.Collections.Concurrent;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LeadingEDJE.Leap.Api.Platform.Services.OpenApi;

/// <summary>
/// Fills an OpenAPI operation's <c>summary</c> and <c>description</c> from the handler's own XML doc
/// comment, for minimal-API handlers the built-in support does not reach because they are private.
/// </summary>
/// <remarks>
/// ASP.NET Core 10's built-in XML comment support caches doc comments at compile time and omits every
/// <c>private</c> symbol. Handlers here must stay <c>private static</c>, so this is a second source
/// rather than a change to that convention: the compiled XML doc file carries every doc-commented
/// symbol regardless of accessibility, and this reads it at runtime, mirroring the built-in lookup
/// algorithm. It only fills a null <c>Summary</c> or <c>Description</c>, and
/// <see cref="BuildDocCommentId"/> declines rather
/// than guesses on a shape it cannot express, so the worst case is no doc comment. Scoped to the
/// published Directory boundary; the admin surface is excluded so internal rationale cannot leak.
/// </remarks>
internal sealed class PrivateHandlerXmlDocOperationTransformer : IOpenApiOperationTransformer
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, XmlDocComment>> DocCommentsByAssembly = new();

    /// <summary>
    /// The published Directory boundary's route prefix, the only surface this transformer may fill.
    /// <c>/api/compass/v1/admin/*</c> shares that prefix but is a surface it must not touch.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> so <c>CompassAuthorizationCoverageTests.IsDirectoryBoundaryReadRoute</c> can
    /// compose its own admin-exclusion filter from this literal instead of an independently
    /// hand-typed <c>"/admin/"</c> -- two literals encoding the same boundary would let one drift
    /// from the other silently if the admin route segment is ever renamed. This is the single source.
    /// </remarks>
    internal const string AdminSurfaceSegment = "/admin/";

    /// <inheritdoc/>
    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (operation.Summary is not null && operation.Description is not null)
        {
            // The built-in generator already answered this operation (a public/internal handler) --
            // nothing for this transformer to add.
            return Task.CompletedTask;
        }

        var relativePath = context.Description.RelativePath;
        if (relativePath is not null
            && relativePath.Contains(AdminSurfaceSegment, StringComparison.OrdinalIgnoreCase))
        {
            // The admin configuration surface -- out of scope, see this class's remarks.
            return Task.CompletedTask;
        }

        var methodInfo = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<MethodInfo>()
            .FirstOrDefault();
        if (methodInfo?.DeclaringType is null)
        {
            return Task.CompletedTask;
        }

        var docId = BuildDocCommentId(methodInfo);
        if (docId is null)
        {
            return Task.CompletedTask;
        }

        var docComments = DocCommentsByAssembly.GetOrAdd(
            methodInfo.DeclaringType.Assembly, LoadDocComments);
        if (docComments.TryGetValue(docId, out var comment))
        {
            operation.Summary ??= comment.Summary;
            operation.Description ??= comment.Description;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the same <c>M:</c> doc-comment ID the compiler would write for this method, for the simple
    /// parameter shapes handlers here use. Returns <c>null</c> rather than a guess for anything else.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> so this ID-building logic -- including the generic-parameter decline branch --
    /// is directly unit-testable, the same reasoning as <see cref="ParseDocComments"/>: constructing a
    /// real ASP.NET Core minimal-API handler with a bare generic parameter through
    /// <see cref="TransformAsync"/>'s endpoint-metadata path is impractical, but the <see cref="MethodInfo"/>
    /// reflection this method needs is trivial to obtain directly.
    /// </remarks>
    internal static string? BuildDocCommentId(MethodInfo method)
    {
        var declaringTypeName = method.DeclaringType?.FullName?.Replace('+', '.');
        if (declaringTypeName is null)
        {
            return null;
        }

        var parameters = method.GetParameters();
        var parameterNames = new string?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameterTypeName = parameters[i].ParameterType.FullName?.Replace('+', '.');
            if (parameterTypeName is null)
            {
                // A generic parameter, an open generic, or another shape this narrow re-implementation
                // does not attempt -- decline rather than emit an ID that cannot match anything real.
                return null;
            }

            parameterNames[i] = parameterTypeName;
        }

        var parameterList = parameterNames.Length == 0 ? string.Empty : $"({string.Join(',', parameterNames)})";
        return $"M:{declaringTypeName}.{method.Name}{parameterList}";
    }

    /// <summary>
    /// Resolves the given assembly's compiled XML documentation file path and parses it via
    /// <see cref="ParseDocComments"/>. Returns an empty map, never throwing, if there is no location.
    /// </summary>
    private static IReadOnlyDictionary<string, XmlDocComment> LoadDocComments(Assembly assembly)
    {
        var location = assembly.Location;
        if (string.IsNullOrEmpty(location))
        {
            return new Dictionary<string, XmlDocComment>(StringComparer.Ordinal);
        }

        return ParseDocComments(Path.ChangeExtension(location, ".xml"));
    }

    /// <summary>
    /// Parses every <c>&lt;member name="M:..."&gt;</c> entry out of the compiled XML documentation file
    /// at <paramref name="xmlPath"/>; empty map, never throwing, if it is missing or malformed.
    /// </summary>
    // Summary-only, and must stay that way: PrivateHandlerXmlDocOperationTransformerTests reads this
    // member's compiled doc entry to prove Description stays null when there is no <remarks>. Hence the
    // rationale sits here rather than in one -- `internal` gives the malformed-file case direct unit
    // coverage, which is impractical to reach through LoadDocComments' assembly-location resolution.
    internal static IReadOnlyDictionary<string, XmlDocComment> ParseDocComments(string xmlPath)
    {
        var result = new Dictionary<string, XmlDocComment>(StringComparer.Ordinal);
        if (!File.Exists(xmlPath))
        {
            return result;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(xmlPath);
        }
        catch (XmlException)
        {
            return result;
        }

        foreach (var member in document.Descendants("member"))
        {
            var name = (string?)member.Attribute("name");
            if (name is not { Length: > 2 } || name[0] != 'M' || name[1] != ':')
            {
                // Only methods back an OpenAPI operation -- types (T:), properties (P:) and fields
                // (F:) are handled by the built-in schema transformer already.
                continue;
            }

            var summary = XmlDocCommentText.Extract(member.Element("summary"));
            var description = XmlDocCommentText.Extract(member.Element("remarks"));
            if (summary is not null || description is not null)
            {
                result[name] = new XmlDocComment(summary, description);
            }
        }

        return result;
    }

    /// <summary><c>internal</c> so <see cref="ParseDocComments"/> is directly unit-testable.</summary>
    internal sealed record XmlDocComment(string? Summary, string? Description);
}
