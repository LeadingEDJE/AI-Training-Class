using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Covers the decorator that makes an email's originating environment visible and gives a
/// non-production environment a way to stop sending altogether (issue #327).
/// </summary>
/// <remarks>
/// Every assertion here goes through <see cref="IEmailSender"/> rather than a template, because the
/// four callers hand this seam three different body shapes: <see cref="EmailTemplates"/> produces a
/// full HTML document, <c>ReportDeliveryService</c> an HTML fragment, and <c>CoachNotifier</c> and
/// <c>OotoEmailService</c> plain text. Stamping inside <c>EmailTemplates.WrapInLayout</c> would label
/// the timesheet mail and silently miss every Compass and OOTO notice.
/// </remarks>
public class EnvironmentStampingEmailSenderTests
{
    /// <summary>
    /// The default is a NON-production host, because that is the environment every assertion in this
    /// class is about: a label only ever appears outside production, and the redirect is keyed on
    /// <see cref="IHostEnvironment.IsProduction"/> rather than on the label (issue #592). The
    /// production cases pass <see cref="Environments.Production"/> explicitly.
    /// </summary>
    private static (EnvironmentStampingEmailSender Sender, MockEmailSender Inner) Build(
        string? environmentLabel,
        bool enabled = true,
        string? redirectAllTo = null,
        string environmentName = "Development",
        string? nonProductionFromAddress = null,
        string? nonProductionFromName = null)
    {
        var inner = new MockEmailSender();

        return (
            BuildFor(
                inner,
                environmentLabel,
                enabled,
                redirectAllTo,
                environmentName,
                NullLogger<EnvironmentStampingEmailSender>.Instance,
                nonProductionFromAddress,
                nonProductionFromName),
            inner);
    }

    /// <summary>
    /// Builds the decorator over a sender that is NOT <see cref="MockEmailSender"/> — the shape that
    /// makes the fail-closed suppression apply, since anything but the in-memory outbox can put mail
    /// on the wire.
    /// </summary>
    private static (
        EnvironmentStampingEmailSender Sender,
        RealTransportSpy Inner,
        CapturingLogger<EnvironmentStampingEmailSender> Logger) BuildOverRealTransport(
        string? environmentLabel,
        string? redirectAllTo = null,
        string environmentName = "Development")
    {
        var inner = new RealTransportSpy();
        var logger = new CapturingLogger<EnvironmentStampingEmailSender>();

        return (
            BuildFor(inner, environmentLabel, true, redirectAllTo, environmentName, logger),
            inner,
            logger);
    }

    private static EnvironmentStampingEmailSender BuildFor(
        IEmailSender inner,
        string? environmentLabel,
        bool enabled,
        string? redirectAllTo,
        string environmentName,
        ILogger<EnvironmentStampingEmailSender> logger,
        string? nonProductionFromAddress = null,
        string? nonProductionFromName = null)
    {
        var options = Options.Create(new EmailEnvironmentOptions
        {
            EnvironmentLabel = environmentLabel,
            Enabled = enabled,
            RedirectAllTo = redirectAllTo,
            NonProductionFromAddress = nonProductionFromAddress,
            NonProductionFromName = nonProductionFromName
        });

        return new EnvironmentStampingEmailSender(
            inner, options, new StubHostEnvironment(environmentName), logger);
    }

    /// <summary>An <see cref="IEmailSender"/> that is not the in-memory outbox, so it counts as a real transport.</summary>
    private sealed class RealTransportSpy : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(string toEmail, string subject, string body, EmailBodyFormat format = EmailBodyFormat.Html,
        EmailFrom? from = null)
        {
            Sent.Add((toEmail, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "LeadingEDJE.Leap.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>Minimal logger capturing level + rendered message so log-level assertions are possible.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task SendAsync_WhenDisabled_NeverReachesTheInnerSender()
    {
        // Arrange
        var (sender, inner) = Build("DEV", enabled: false);

        // Act
        await sender.SendAsync("coach@leadingedje.com", "Assignment Change", "<p>Body</p>");

        // Assert
        inner.SentEmails.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendAsync_WhenDisabled_StillCompletesSoTheCallingWriteStands()
    {
        // Arrange -- CoachNotifier reaches this seam AFTER its write has committed, so a suppressed
        // send must not throw. A disabled mailer that faults would turn a successful assignment
        // change into an HTTP 500 describing an email.
        var (sender, _) = Build("DEV", enabled: false);

        // Act
        var send = async () => await sender.SendAsync("coach@leadingedje.com", "Subject", "Body");

        // Assert
        await send.ShouldNotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_WithNoEnvironmentLabel_PassesSubjectAndBodyThroughUntouched()
    {
        // Arrange -- production carries no label, and its mail must be byte-identical to today's.
        var (sender, inner) = Build(environmentLabel: null);

        // Act
        await sender.SendAsync("hr@leadingedje.com", "Timesheet Reminder", "<p>Please submit</p>");

        // Assert
        var sent = inner.SentEmails.ShouldHaveSingleItem();
        sent.Subject.ShouldBe("Timesheet Reminder");
        sent.Body.ShouldBe("<p>Please submit</p>");
    }

    [Fact]
    public async Task SendAsync_WithWhitespaceEnvironmentLabel_PassesThroughUntouched()
    {
        // Arrange -- Helm renders an unset value as "", and a quoted empty string must not produce
        // a "[] Subject" prefix on production mail.
        var (sender, inner) = Build("   ");

        // Act
        await sender.SendAsync("hr@leadingedje.com", "Timesheet Reminder", "Please submit");

        // Assert
        var sent = inner.SentEmails.ShouldHaveSingleItem();
        sent.Subject.ShouldBe("Timesheet Reminder");
        sent.Body.ShouldBe("Please submit");
    }

    [Fact]
    public async Task SendAsync_WithEnvironmentLabel_PrefixesTheSubject()
    {
        // Arrange
        var (sender, inner) = Build("DEV");

        // Act
        await sender.SendAsync("coach@leadingedje.com", "Kim Assignment Change", "Body");

        // Assert
        var sent = inner.SentEmails.ShouldHaveSingleItem();
        sent.Subject.ShouldBe("[DEV] Kim Assignment Change");
    }

    [Fact]
    public async Task SendAsync_WithEnvironmentLabel_DoesNotPrefixASubjectThatAlreadyCarriesIt()
    {
        // Arrange -- the retry worker re-sends a stored notice, and a second pass must not produce
        // "[DEV] [DEV] ...".
        var (sender, inner) = Build("DEV");

        // Act
        await sender.SendAsync("coach@leadingedje.com", "[DEV] Kim Assignment Change", "Body");

        // Assert
        var sent = inner.SentEmails.ShouldHaveSingleItem();
        sent.Subject.ShouldBe("[DEV] Kim Assignment Change");
    }

    [Fact]
    public async Task SendAsync_WithEnvironmentLabel_AndNoRedirect_LeavesTheRecipientAlone()
    {
        // Arrange -- with no redirect configured and the in-memory outbox behind it, nothing can leave
        // the machine, so rerouting would only hide who the message was for. The fail-closed
        // suppression (issue #592) applies to a REAL transport, not to this one.
        var (sender, inner) = Build("DEV");

        // Act
        await sender.SendAsync("coach@leadingedje.com", "Subject", "Body");

        // Assert
        inner.SentEmails.ShouldHaveSingleItem().To.ShouldBe("coach@leadingedje.com");
    }

    [Fact]
    public async Task SendAsync_WithEnvironmentLabel_BannersAFullHtmlDocumentInsideTheBodyTag()
    {
        // Arrange -- EmailTemplates hands over a complete document. A banner prepended ahead of
        // the doctype is markup before the doctype, which clients are free to drop.
        var (sender, inner) = Build("DEV");
        var html =
            "<!DOCTYPE html>\n<html>\n<head><meta charset=\"utf-8\" /></head>\n"
            + "<body style=\"font-family: Arial;\">\n  <p>Hello</p>\n</body>\n</html>";

        // Act
        await sender.SendAsync("hr@leadingedje.com", "Subject", html);

        // Assert
        var body = inner.SentEmails.ShouldHaveSingleItem().Body;
        body.ShouldStartWith("<!DOCTYPE html>");
        body.ShouldContain("DEV");
        body.IndexOf("DEV", StringComparison.Ordinal)
            .ShouldBeLessThan(body.IndexOf("<p>Hello</p>", StringComparison.Ordinal));
        body.ShouldContain("<p>Hello</p>");
    }

    [Fact]
    public async Task SendAsync_WithEnvironmentLabel_BannersAnHtmlFragment()
    {
        // Arrange -- ReportDeliveryService sends fragments with no body tag to inject into.
        var (sender, inner) = Build("DEV");

        // Act
        await sender.SendAsync(
            "accounting@leadingedje.com", "Subject", "<p>A new report has been generated.</p>");

        // Assert
        var body = inner.SentEmails.ShouldHaveSingleItem().Body;
        body.ShouldContain("DEV");
        body.ShouldContain("<p>A new report has been generated.</p>");
        body.IndexOf("DEV", StringComparison.Ordinal)
            .ShouldBeLessThan(body.IndexOf("A new report", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_WithEnvironmentLabel_PrependsAPlainTextLineToAPlainTextBody()
    {
        // Arrange -- CoachNotifier's body is a bare sentence. An HTML banner on it would render as
        // visible angle brackets in the recipient's client.
        var (sender, inner) = Build("DEV");

        // Act
        await sender.SendAsync(
            "coach@leadingedje.com",
            "Subject",
            "Kim assignment at Acme is coming to an end on 09/30/2026",
            EmailBodyFormat.PlainText);

        // Assert
        var body = inner.SentEmails.ShouldHaveSingleItem().Body;
        body.ShouldNotContain("<");
        body.ShouldContain("DEV");
        body.ShouldContain("Kim assignment at Acme is coming to an end on 09/30/2026");
        body.ShouldStartWith("[DEV]");
    }

    [Fact]
    public async Task SendAsync_WithEnvironmentLabel_EncodesTheLabelIntoTheHtmlBanner()
    {
        // Arrange -- the label is operator-supplied config (the deploy workflow derives the preview
        // one from a PR title), so it reaches an HTML body as untrusted text.
        var (sender, inner) = Build("PR #42 <script>alert(1)</script>");

        // Act
        await sender.SendAsync("hr@leadingedje.com", "Subject", "<p>Hello</p>");

        // Assert
        var body = inner.SentEmails.ShouldHaveSingleItem().Body;
        body.ShouldNotContain("<script>");
        body.ShouldContain("&lt;script&gt;");
    }

    [Fact]
    public async Task SendAsync_WithEnvironmentLabel_NamesTheEnvironmentInThePlainTextBanner()
    {
        // Arrange -- the point of the whole feature: the reader can tell WHICH environment sent it.
        var (sender, inner) = Build("PR #42");

        // Act
        await sender.SendAsync("coach@leadingedje.com", "Subject", "A plain sentence.");

        // Assert
        var sent = inner.SentEmails.ShouldHaveSingleItem();
        sent.Subject.ShouldBe("[PR #42] Subject");
        sent.Body.ShouldContain("PR #42");
    }

    // ---------------------------------------------------------------------------------------------
    // Issue #592: non-production recipient redirect, and the fail-closed suppression behind it.
    //
    // The redirect is keyed on IHostEnvironment.IsProduction(), NOT on EnvironmentLabel. The two are
    // separate concepts and the tests below hold them apart deliberately: the label answers "which
    // non-production environment is this" (deployed dev and every PR preview are both
    // ASPNETCORE_ENVIRONMENT=Staging, so the host cannot answer that), while the redirect answers "is
    // it safe to mail a real address" -- for which an unset label must NOT read as production.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task SendAsync_InNonProduction_WithARedirect_DeliversOnlyToTheRedirectAddress()
    {
        // Arrange -- Compass's migrated dev addresses are FABRICATED first.last@leadingedje.com values
        // (#544), so they are plausible real colleagues. Every recipient is redirected; there is no
        // domain allowlist, because a domain rule cannot tell a fabricated address from a real one.
        var (sender, inner) = Build("DEV", redirectAllTo: "leap-dev-sink@leadingedje.com");

        // Act
        await sender.SendAsync("kim.jones@leadingedje.com", "Assignment Change", "Body");

        // Assert
        inner.SentEmails.ShouldHaveSingleItem().To.ShouldBe("leap-dev-sink@leadingedje.com");
    }

    // ---------------------------------------------------------------------------------------------
    // #327's label has to SURVIVE #592's reroute, and the three tests below are the only place that
    // is pinned. Every other label assertion in this class exercises the no-redirect branch, which
    // delivers to the in-memory outbox -- and once #589 supplies the SMTP credentials, the redirect
    // branch is the ONLY path deployed non-production mail takes. Without these, #327's feature is
    // untested in its only live configuration: dropping `stampedSubject` for `subject`, or passing a
    // null label to DecorateBody, killed ZERO of the 32 tests that preceded them.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task SendAsync_InNonProduction_WithARedirect_KeepsTheEnvironmentLabelOnTheSubject()
    {
        // Arrange
        var (sender, inner) = Build("DEV", redirectAllTo: "leap-dev-sink@leadingedje.com");

        // Act
        await sender.SendAsync("kim.jones@leadingedje.com", "Assignment Change", "Body");

        // Assert
        inner.SentEmails.ShouldHaveSingleItem().Subject.ShouldBe("[DEV] Assignment Change");
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithARedirect_KeepsTheEnvironmentBannerInPlainText()
    {
        // Arrange -- CoachNotifier's plain-text body, redirected. The banner and the intended-recipient
        // line are one notice, so the label must not be the half that goes missing.
        var (sender, inner) = Build("DEV", redirectAllTo: "leap-dev-sink@leadingedje.com");

        // Act
        await sender.SendAsync(
            "kim.jones@leadingedje.com", "Subject", "Kim assignment at Acme ends 09/30/2026",
            EmailBodyFormat.PlainText);

        // Assert
        var body = inner.SentEmails.ShouldHaveSingleItem().Body;
        body.ShouldStartWith("[DEV] This message was sent from the DEV environment.");
        body.ShouldContain("kim.jones@leadingedje.com");
        body.ShouldContain("Kim assignment at Acme ends 09/30/2026");
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithARedirect_KeepsTheEnvironmentBannerInHtml()
    {
        // Arrange -- EmailTemplates' full document, redirected.
        var (sender, inner) = Build("DEV", redirectAllTo: "leap-dev-sink@leadingedje.com");
        var html =
            "<!DOCTYPE html>\n<html>\n<head><meta charset=\"utf-8\" /></head>\n"
            + "<body style=\"font-family: Arial;\">\n  <p>Hello</p>\n</body>\n</html>";

        // Act
        await sender.SendAsync("kim.jones@leadingedje.com", "Subject", html);

        // Assert -- both lines, in ONE banner. Two banners would mean the label and the recipient had
        // been decorated in separate passes, which on this body shape puts the second ahead of the
        // first (see the remarks on DecorateBody).
        var body = inner.SentEmails.ShouldHaveSingleItem().Body;
        body.ShouldContain("DEV environment &mdash; this message did not come from production.");
        body.ShouldContain("kim.jones@leadingedje.com");
        CountOccurrences(body, "background-color: #b45309").ShouldBe(1);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;

        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithARedirect_NamesTheIntendedRecipientInPlainText()
    {
        // Arrange -- a tester has to be able to see who the message would have reached.
        var (sender, inner) = Build("DEV", redirectAllTo: "leap-dev-sink@leadingedje.com");

        // Act
        await sender.SendAsync(
            "kim.jones@leadingedje.com", "Subject", "Kim assignment at Acme ends 09/30/2026",
            EmailBodyFormat.PlainText);

        // Assert -- plain text in, plain text out. An HTML notice here renders as angle brackets.
        var body = inner.SentEmails.ShouldHaveSingleItem().Body;
        body.ShouldNotContain("<");
        body.ShouldContain("kim.jones@leadingedje.com");
        body.ShouldContain("Kim assignment at Acme ends 09/30/2026");
        body.IndexOf("kim.jones@leadingedje.com", StringComparison.Ordinal)
            .ShouldBeLessThan(body.IndexOf("Kim assignment", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithARedirect_NamesTheIntendedRecipientInsideTheBodyTag()
    {
        // Arrange -- EmailTemplates hands over a complete document; the notice belongs INSIDE <body>,
        // because markup ahead of the doctype is droppable.
        var (sender, inner) = Build("DEV", redirectAllTo: "leap-dev-sink@leadingedje.com");
        var html =
            "<!DOCTYPE html>\n<html>\n<head><meta charset=\"utf-8\" /></head>\n"
            + "<body style=\"font-family: Arial;\">\n  <p>Hello</p>\n</body>\n</html>";

        // Act
        await sender.SendAsync("kim.jones@leadingedje.com", "Subject", html);

        // Assert
        var body = inner.SentEmails.ShouldHaveSingleItem().Body;
        body.ShouldStartWith("<!DOCTYPE html>");
        body.ShouldContain("kim.jones@leadingedje.com");
        body.IndexOf("kim.jones@leadingedje.com", StringComparison.Ordinal)
            .ShouldBeLessThan(body.IndexOf("<p>Hello</p>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithARedirect_NamesTheIntendedRecipientInAnHtmlFragment()
    {
        // Arrange -- ReportDeliveryService sends fragments with no <body> to enter.
        var (sender, inner) = Build("DEV", redirectAllTo: "leap-dev-sink@leadingedje.com");

        // Act
        await sender.SendAsync(
            "accounting@leadingedje.com", "Subject", "<p>A new report has been generated.</p>");

        // Assert
        var body = inner.SentEmails.ShouldHaveSingleItem().Body;
        body.ShouldContain("accounting@leadingedje.com");
        body.ShouldContain("<p>A new report has been generated.</p>");
        body.IndexOf("accounting@leadingedje.com", StringComparison.Ordinal)
            .ShouldBeLessThan(body.IndexOf("A new report", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithARedirect_EncodesTheIntendedRecipientIntoTheHtml()
    {
        // Arrange -- the address comes from the directory, not from an operator, so it reaches an HTML
        // body as untrusted text exactly as the label does.
        var (sender, inner) = Build("DEV", redirectAllTo: "leap-dev-sink@leadingedje.com");

        // Act
        await sender.SendAsync("<script>alert(1)</script>@x.test", "Subject", "<p>Hello</p>");

        // Assert
        var body = inner.SentEmails.ShouldHaveSingleItem().Body;
        body.ShouldNotContain("<script>");
        body.ShouldContain("&lt;script&gt;");
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithARedirectAndNoLabel_StillRedirects()
    {
        // Arrange -- THE reason the redirect is not keyed on the label. A new environment that forgets
        // to set a label must still be safe; an empty label means "do not stamp", never "production".
        var (sender, inner) = Build(
            environmentLabel: null, redirectAllTo: "leap-dev-sink@leadingedje.com");

        // Act
        await sender.SendAsync("kim.jones@leadingedje.com", "Subject", "Body");

        // Assert
        var sent = inner.SentEmails.ShouldHaveSingleItem();
        sent.To.ShouldBe("leap-dev-sink@leadingedje.com");
        sent.Subject.ShouldBe("Subject");
        sent.Body.ShouldContain("kim.jones@leadingedje.com");
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithARealTransportAndNoRedirect_SuppressesTheSend()
    {
        // Arrange -- fail closed. Today this path delivers to the real address; the cost of the safe
        // default is one environment variable.
        var (sender, inner, _) = BuildOverRealTransport("DEV", redirectAllTo: null);

        // Act
        await sender.SendAsync("kim.jones@leadingedje.com", "Assignment Change", "Body");

        // Assert
        inner.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithAWhitespaceRedirect_SuppressesRatherThanSending()
    {
        // Arrange -- Helm renders a stray-space value verbatim, and "   " is not an address. The
        // `?.Trim()` in front of the IsNullOrEmpty check is the only thing between that value and a
        // SES Destination of "   " on every non-production send, which SES refuses. Treated as
        // unconfigured, so the send fails closed exactly as an absent value does.
        var (sender, inner, logger) = BuildOverRealTransport("DEV", redirectAllTo: "   ");

        // Act
        var send = async () =>
            await sender.SendAsync("kim.jones@leadingedje.com", "Assignment Change", "Body");

        // Assert
        await send.ShouldNotThrowAsync();
        inner.Sent.ShouldBeEmpty();
        logger.Entries.ShouldHaveSingleItem().Message.ShouldContain("RedirectAllTo");
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithARealTransportAndNoRedirect_LogsLoudly()
    {
        // Arrange -- a silent drop is its own outage. This has to be findable in the log, and it has to
        // name the key that fixes it.
        var (sender, _, logger) = BuildOverRealTransport("DEV", redirectAllTo: null);

        // Act
        await sender.SendAsync("kim.jones@leadingedje.com", "Assignment Change", "Body");

        // Assert
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBeOneOf(LogLevel.Warning, LogLevel.Error, LogLevel.Critical);
        entry.Message.ShouldContain("RedirectAllTo");
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithARealTransportAndNoRedirect_StillCompletes()
    {
        // Arrange -- same reason as the kill switch: CoachNotifier reaches this seam after its write
        // has committed, so a suppressed send must not turn a successful change into an HTTP 500.
        var (sender, _, _) = BuildOverRealTransport("DEV", redirectAllTo: null);

        // Act
        var send = async () => await sender.SendAsync("kim.jones@leadingedje.com", "Subject", "Body");

        // Assert
        await send.ShouldNotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithTheInMemoryOutboxAndNoRedirect_StillDelivers()
    {
        // Arrange -- MockEmailSender cannot put mail on the wire, so suppressing here would buy no
        // safety and would blind local dev and the Compass outbox assertions.
        var (sender, inner) = Build("DEV", redirectAllTo: null);

        // Act
        await sender.SendAsync("kim.jones@leadingedje.com", "Subject", "Body");

        // Assert
        inner.SentEmails.ShouldHaveSingleItem().To.ShouldBe("kim.jones@leadingedje.com");
    }

    [Fact]
    public async Task SendAsync_InProduction_WithARedirectConfigured_StillMailsTheRealRecipient()
    {
        // Arrange -- the guard that must never quietly invert. A redirect address left in production
        // configuration must not divert production mail to a dev sink.
        var (sender, inner) = Build(
            environmentLabel: null,
            redirectAllTo: "leap-dev-sink@leadingedje.com",
            environmentName: Environments.Production);

        // Act
        await sender.SendAsync(
            "kim.jones@leadingedje.com", "Timesheet Reminder", "<p>Please submit</p>");

        // Assert
        var sent = inner.SentEmails.ShouldHaveSingleItem();
        sent.To.ShouldBe("kim.jones@leadingedje.com");
        sent.Subject.ShouldBe("Timesheet Reminder");
        sent.Body.ShouldBe("<p>Please submit</p>");
    }

    [Fact]
    public async Task SendAsync_InProduction_WithARealTransportAndNoRedirect_IsNeverSuppressed()
    {
        // Arrange -- production has no redirect and must never fail closed; suppressing here would
        // silently stop all production mail.
        var (sender, inner, logger) = BuildOverRealTransport(
            environmentLabel: null, redirectAllTo: null, environmentName: Environments.Production);

        // Act
        await sender.SendAsync(
            "kim.jones@leadingedje.com", "Timesheet Reminder", "<p>Please submit</p>");

        // Assert
        var sent = inner.Sent.ShouldHaveSingleItem();
        sent.To.ShouldBe("kim.jones@leadingedje.com");
        sent.Subject.ShouldBe("Timesheet Reminder");
        sent.Body.ShouldBe("<p>Please submit</p>");
        logger.Entries.ShouldBeEmpty("production must not gain new log noise from issue #592");
    }

    // The non-production From sender. Dev's account verifies only the dev.leadingedje.com SES
    // identity, so a production From is refused by SES and by the role's ses:FromAddress condition.
    // The rewrite is here rather than in CoachNotifier because AC-43/FR-029 requires that caller's
    // address verbatim, and it keys on IsProduction() rather than the label. Detail on
    // EmailEnvironmentOptions.NonProductionFromAddress.

    /// <summary>The dev SES identity — the one address dev's AWS account can actually send as.</summary>
    private const string DevFrom = "no-reply@dev.leadingedje.com";

    /// <summary>CoachNotifier's verbatim AC-43 sender, address-only. The caller still asks for this.</summary>
    private static readonly EmailFrom CompassSender = new("no-reply@leadingedje.com");

    [Fact]
    public async Task SendAsync_InNonProduction_WithAFromOverride_ReplacesANullFrom()
    {
        // Arrange -- the subtle case. A null `from` is not "no sender": it means "use the configured
        // default", which resolves to Email:FromAddress = timesheet@leadingedje.com, a production
        // address that dev has not verified as an identity. So null must become the override rather
        // than stay null.
        var (sender, inner) = Build("DEV", nonProductionFromAddress: DevFrom);

        // Act
        await sender.SendAsync("hr@leadingedje.com", "Timesheet Reminder", "Please submit");

        // Assert
        var sent = inner.SentEmails.ShouldHaveSingleItem();
        sent.From.ShouldNotBeNull();
        sent.From.Address.ShouldBe(DevFrom);
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithAFromOverride_ReplacesASuppliedFrom()
    {
        // Arrange -- CoachNotifier asks for no-reply@leadingedje.com per AC-43 and keeps asking; the
        // decorator rewrites it for delivery only, so SES is not handed an unverified identity.
        var (sender, inner) = Build("DEV", nonProductionFromAddress: DevFrom);

        // Act
        await sender.SendAsync("coach@leadingedje.com", "Assignment Change", "Body", from: CompassSender);

        // Assert
        var sent = inner.SentEmails.ShouldHaveSingleItem();
        sent.From.ShouldNotBeNull();
        sent.From.Address.ShouldBe(DevFrom);
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithAFromOverrideAndAName_AppliesBothHalves()
    {
        // Arrange -- EmailFrom is all-or-nothing by contract (see its remarks): an override never
        // mixes its address with the caller's display name.
        var (sender, inner) = Build(
            "DEV", nonProductionFromAddress: DevFrom, nonProductionFromName: "LEAP DEV");

        // Act
        await sender.SendAsync(
            "coach@leadingedje.com",
            "Assignment Change",
            "Body",
            from: new EmailFrom("no-reply@leadingedje.com", "LeadingEDJE Compass"));

        // Assert
        var sent = inner.SentEmails.ShouldHaveSingleItem();
        sent.From.ShouldNotBeNull();
        sent.From.Address.ShouldBe(DevFrom);
        sent.From.DisplayName.ShouldBe("LEAP DEV");
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithAFromOverrideAndNoName_SendsAddressOnly()
    {
        // Arrange -- an absent name means an address-only From, exactly as a bare EmailFrom does. It
        // must not fall back to the caller's name, or to Email:FromName.
        var (sender, inner) = Build("DEV", nonProductionFromAddress: DevFrom);

        // Act
        await sender.SendAsync(
            "coach@leadingedje.com",
            "Assignment Change",
            "Body",
            from: new EmailFrom("no-reply@leadingedje.com", "LeadingEDJE Compass"));

        // Assert
        var sent = inner.SentEmails.ShouldHaveSingleItem();
        sent.From.ShouldNotBeNull();
        sent.From.DisplayName.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithAWhitespaceFromName_SendsAddressOnly()
    {
        // Arrange -- Helm renders a stray-space value verbatim, and "   " is not a display name.
        var (sender, inner) = Build(
            "DEV", nonProductionFromAddress: DevFrom, nonProductionFromName: "   ");

        // Act
        await sender.SendAsync("hr@leadingedje.com", "Subject", "Body");

        // Assert
        inner.SentEmails.ShouldHaveSingleItem().From.ShouldNotBeNull().DisplayName
            .ShouldBe(string.Empty);
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithARedirect_AlsoReplacesTheFrom()
    {
        // Arrange -- the redirect branch is a second send path, and with a sink address configured
        // it is the only path deployed non-production mail takes. A rewrite applied to the outbox
        // path alone would be dead in every deployed environment.
        var (sender, inner) = Build(
            "DEV",
            redirectAllTo: "leap-dev-sink@leadingedje.com",
            nonProductionFromAddress: DevFrom,
            nonProductionFromName: "LEAP DEV");

        // Act
        await sender.SendAsync(
            "kim.jones@leadingedje.com", "Assignment Change", "Body", from: CompassSender);

        // Assert
        var sent = inner.SentEmails.ShouldHaveSingleItem();
        sent.To.ShouldBe("leap-dev-sink@leadingedje.com");
        sent.From.ShouldNotBeNull();
        sent.From.Address.ShouldBe(DevFrom);
        sent.From.DisplayName.ShouldBe("LEAP DEV");
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithARedirectAndANullFrom_StillReplacesIt()
    {
        // Arrange -- the redirect path's version of the null case. Same reasoning: null resolves to
        // the production Email:FromAddress downstream.
        var (sender, inner) = Build(
            "DEV",
            redirectAllTo: "leap-dev-sink@leadingedje.com",
            nonProductionFromAddress: DevFrom);

        // Act
        await sender.SendAsync("kim.jones@leadingedje.com", "Timesheet Reminder", "Please submit");

        // Assert
        inner.SentEmails.ShouldHaveSingleItem().From.ShouldNotBeNull().Address.ShouldBe(DevFrom);
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithNoFromOverride_LeavesANullFromNull()
    {
        // Arrange -- an environment that configures neither key behaves exactly as it does today.
        // Null must stay null so the inner sender still applies its configured default.
        var (sender, inner) = Build("DEV");

        // Act
        await sender.SendAsync("hr@leadingedje.com", "Timesheet Reminder", "Please submit");

        // Assert
        inner.SentEmails.ShouldHaveSingleItem().From.ShouldBeNull();
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithNoFromOverride_LeavesASuppliedFromAlone()
    {
        // Arrange -- the local-dev and Compass-outbox shape: no override configured, so AC-43's
        // sender reaches the outbox unchanged and CoachNotificationTests keeps asserting it.
        var (sender, inner) = Build("DEV");

        // Act
        await sender.SendAsync("coach@leadingedje.com", "Assignment Change", "Body", from: CompassSender);

        // Assert
        inner.SentEmails.ShouldHaveSingleItem().From.ShouldBe(CompassSender);
    }

    [Fact]
    public async Task SendAsync_InNonProduction_WithAWhitespaceFromOverride_LeavesTheFromAlone()
    {
        // Arrange -- Helm renders an unset value as "", and values-production.yaml pins both keys to
        // "" explicitly. Whitespace is treated as unconfigured, the same way RedirectAllTo's Trim()
        // treats it -- never as an address to send as, which paired with a display name would throw
        // in the MailAddress the SES Source is formatted through.
        var (sender, inner) = Build(
            "DEV", nonProductionFromAddress: "   ", nonProductionFromName: "LEAP DEV");

        // Act
        await sender.SendAsync("coach@leadingedje.com", "Assignment Change", "Body", from: CompassSender);

        // Assert
        inner.SentEmails.ShouldHaveSingleItem().From.ShouldBe(CompassSender);
    }

    [Fact]
    public async Task SendAsync_InProduction_WithAFromOverrideConfigured_NeverReplacesASuppliedFrom()
    {
        // Arrange -- the guard that must never quietly invert, for the same reason production ignores
        // a stray RedirectAllTo: a production From must not be rewritable by leftover dev config.
        // AC-43 requires production to send from no-reply@leadingedje.com.
        var (sender, inner) = Build(
            environmentLabel: null,
            environmentName: Environments.Production,
            nonProductionFromAddress: DevFrom,
            nonProductionFromName: "LEAP DEV");

        // Act
        await sender.SendAsync(
            "kim.jones@leadingedje.com", "Assignment Change", "Body", from: CompassSender);

        // Assert
        inner.SentEmails.ShouldHaveSingleItem().From.ShouldBe(CompassSender);
    }

    [Fact]
    public async Task SendAsync_InProduction_WithAFromOverrideConfigured_LeavesANullFromNull()
    {
        // Arrange -- the other half of the production guard. Null must stay null so Timesheet mail
        // keeps leaving from Email:FromAddress.
        var (sender, inner) = Build(
            environmentLabel: null,
            environmentName: Environments.Production,
            nonProductionFromAddress: DevFrom,
            nonProductionFromName: "LEAP DEV");

        // Act
        await sender.SendAsync("kim.jones@leadingedje.com", "Timesheet Reminder", "Please submit");

        // Assert
        inner.SentEmails.ShouldHaveSingleItem().From.ShouldBeNull();
    }

    /// <remarks>
    /// AnyTagRegex was `<[a-z!/][^>]*>`, which matches a bare address in angle brackets, so a
    /// plain-text body naming a recipient took the HTML-banner path and the reader saw the markup
    /// (#598). The declared format settles it without a smarter regex.
    /// </remarks>
    [Fact]
    public async Task SendAsync_PlainTextNamingAnAddressInAngleBrackets_GetsThePlainTextNotice()
    {
        // Arrange
        var (sender, inner, _) = BuildOverRealTransport("DEV", redirectAllTo: "sink@leadingedje.com");

        // Act
        await sender.SendAsync(
            "coach@leadingedje.com",
            "Assignment Change",
            "Coach is <kim@leadingedje.com> for this assignment.",
            EmailBodyFormat.PlainText);

        // Assert
        var sent = inner.Sent.ShouldHaveSingleItem();
        sent.Body.ShouldNotContain(
            "<div style=",
            customMessage: "a plain-text body must not receive an HTML banner -- the reader sees the tags");
        sent.Body.ShouldContain(
            "[DEV]",
            customMessage: "the plain-text notice still has to say which environment sent it");
    }
}
