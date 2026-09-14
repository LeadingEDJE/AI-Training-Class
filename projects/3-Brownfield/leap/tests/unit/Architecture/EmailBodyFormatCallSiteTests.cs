using System.Text.RegularExpressions;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Every production send states what its body is, so the default can never decide it.
/// </summary>
/// <remarks>
/// <c>IEmailSender.SendAsync</c> defaults <c>format</c> to <c>Html</c>, which existed only to make
/// adding the parameter a no-op. A caller that leans on that default has re-created the sniffing the
/// parameter replaced, one indirection back, and would send a plain-text name containing <c>&amp;</c>
/// as a broken entity (#598). Structure mirrors <see cref="EmailConfigurationKeyParityTests"/>:
/// walk the sources, assert non-vacuity first, then the property.
/// </remarks>
public partial class EmailBodyFormatCallSiteTests
{
    /// <summary>
    /// A floor on the call sites found, so a scan that matches nothing cannot pass. Five today:
    /// CoachNotifier, NotificationRetryJob, plus three in EnvironmentStampingEmailSender (production,
    /// in-memory-outbox and redirect-to paths). OotoEmailService, MondayUnsubmittedReportJob,
    /// ReportDeliveryService and NotificationService are gone with the Timesheet/Ooto modules.
    /// Re-derive it by running <see cref="TheScan_FindsTheSendCallSites_SoAnEmptySweepCannotPass"/> and
    /// counting; never by lowering it to whatever the scan happened to return.
    /// </summary>
    private const int KnownSendCallSiteFloor = 5;

    [Fact]
    public void TheScan_FindsTheSendCallSites_SoAnEmptySweepCannotPass()
    {
        // Arrange & Act
        var sites = SendCallSites();

        // Assert
        sites.Count.ShouldBeGreaterThanOrEqualTo(
            KnownSendCallSiteFloor,
            $"only {sites.Count} SendAsync call sites were found in api/. A gate that sweeps nothing "
                + "passes, so this floor is the thing that fails instead.");
    }

    [Fact]
    public void EveryProductionSend_DeclaresItsBodyFormat()
    {
        // Arrange
        var sites = SendCallSites();
        sites.ShouldNotBeEmpty("the scan found nothing, so the assertion below would be vacuous");

        // Act
        var implicitSites = sites
            .Where(site => !DeclaresFormat(site.Arguments))
            .Select(site => $"{site.File}:{site.Line}")
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        implicitSites.ShouldBeEmpty(
            "these sends inherit the Html default instead of declaring their body's format, which is "
                + $"how a plain-text body gets delivered as markup: {string.Join(", ", implicitSites)}");
    }

    private sealed record CallSite(string File, int Line, string Arguments);

    /// <summary>
    /// Whether a call site states the body's format, either as a literal or by forwarding its own.
    /// </summary>
    /// <remarks>
    /// A decorator is not a caller: <c>EnvironmentStampingEmailSender</c> passes the format it was
    /// given straight through, and demanding a literal there would force it to invent one — which is
    /// the opposite of the point. So a bare <c>format</c> argument counts, and only a site that
    /// mentions neither is inheriting the default.
    /// </remarks>
    private static bool DeclaresFormat(string arguments) =>
        arguments.Contains("EmailBodyFormat.", StringComparison.Ordinal)
        || ForwardedFormatArgument().IsMatch(arguments);

    [GeneratedRegex(@"(^|,)\s*(format:\s*)?format\s*(,|$)")]
    private static partial Regex ForwardedFormatArgument();

    /// <summary>
    /// Every <c>SendAsync(</c> invocation on an email sender under <c>api/</c>, with its argument list.
    /// </summary>
    /// <remarks>
    /// Balanced-paren extraction rather than a line regex, because four of the sites span lines and a
    /// line-anchored pattern would silently skip them — the failure that makes a gate look green. The
    /// interface declaration and the implementations' own signatures are excluded by requiring a
    /// receiver before the call.
    /// </remarks>
    private static List<CallSite> SendCallSites()
    {
        var sites = new List<CallSite>();

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            var relative = Path.GetRelativePath(RepositoryRoot(), file)
                .Replace(Path.DirectorySeparatorChar, '/');

            for (var i = text.IndexOf(".SendAsync(", StringComparison.Ordinal); i >= 0;
                 i = text.IndexOf(".SendAsync(", i + 1, StringComparison.Ordinal))
            {
                var open = text.IndexOf('(', i);
                var depth = 0;
                var end = open;

                for (; end < text.Length; end++)
                {
                    if (text[end] == '(')
                    {
                        depth++;
                    }
                    else if (text[end] == ')' && --depth == 0)
                    {
                        break;
                    }
                }

                var arguments = text[(open + 1)..Math.Min(end, text.Length)];

                // Slack's client has a SendDirectMessageAsync, but ISlackClient also carries no
                // SendAsync -- so anything matched here is an email send. Recipient-shaped first
                // argument is not required: the retry job passes a stored column.
                sites.Add(new CallSite(
                    relative,
                    text[..i].Count(c => c == '\n') + 1,
                    arguments));
            }
        }

        return sites;
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Absolute("api"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));

    private static string Absolute(string relativePath)
        => Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    // Walks up from the test assembly until the solution file appears. Throws rather than returning a
    // best guess: a wrong root would make the scan find nothing, which reads as a passing gate.
    private static string RepositoryRoot()
    {
        const string SolutionFile = "leap.slnx";

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"The email body-format call-site check could not locate '{SolutionFile}' walking up "
                + $"from '{AppContext.BaseDirectory}'.");
    }
}
