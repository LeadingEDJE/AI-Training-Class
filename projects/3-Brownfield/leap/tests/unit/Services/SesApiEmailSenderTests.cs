using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Covers the SES API transport — the request it builds, the sender it resolves, the region it builds
/// its client with, and what it does when SES refuses a send.
/// </summary>
/// <remarks>
/// The HTTPS conversation with SES and credential resolution are deliberately not covered: both need
/// AWS. Everything asserted here is a decision the sender makes before the request leaves the
/// process, which is where the configuration bugs have been. SES v1 (<c>Amazon.SimpleEmail</c>) is a
/// constraint rather than a preference — the <c>ses:FromAddress</c> condition that is the role's whole
/// permission boundary applies to the v1 actions. Detail:
/// <c>docs/ops/email-environment-labelling.md</c>.
/// </remarks>
public class SesApiEmailSenderTests
{
    private const string Region = "us-east-1";

    private static readonly SesEmailSettings DefaultSettings = new(
        Region: Region,
        FromAddress: "timesheet@leadingedje.com",
        FromName: "LeadingEDJE Timesheet");

    private static IConfiguration Config(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private static IConfiguration WithRegion(params (string Key, string? Value)[] settings) =>
        Config([("Email:SesRegion", Region), .. settings]);

    /// <summary>
    /// A stand-in for the SES client that records the request instead of sending it, or throws.
    /// </summary>
    /// <remarks>
    /// Derived from the generated client rather than implementing <see cref="IAmazonSimpleEmailService"/>,
    /// which carries some forty operations. The region-only base constructor resolves no credentials
    /// (AWS SDK v4 defers that to the first request), and <c>SendEmailAsync</c> is overridden, so
    /// nothing here can reach the network.
    /// </remarks>
    private sealed class FakeSesClient(Exception? failWith = null)
        : AmazonSimpleEmailServiceClient(Amazon.RegionEndpoint.GetBySystemName(Region))
    {
        public List<SendEmailRequest> Requests { get; } = [];

        public override Task<SendEmailResponse> SendEmailAsync(
            SendEmailRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return failWith is not null
                ? Task.FromException<SendEmailResponse>(failWith)
                : Task.FromResult(new SendEmailResponse { MessageId = "0100-fake-message-id" });
        }
    }

    private static SesApiEmailSender Sender(IAmazonSimpleEmailService ses, IConfiguration config) =>
        new(ses, config, NullLogger<SesApiEmailSender>.Instance);

    // ------------------------------------------------------------------ the request handed to SES

    [Fact]
    public void BuildRequest_CarriesTheDestinationSubjectAndBothBodyParts()
    {
        // Arrange
        var html = "<html><body><p>Hello <strong>World</strong></p></body></html>";

        // Act
        var request = SesApiEmailSender.BuildRequest(
            "from@test.com", "Test Sender", "to@test.com", "Test Subject", html);

        // Assert
        request.Destination.ToAddresses.ShouldBe(["to@test.com"]);
        request.Message.Subject.Data.ShouldBe("Test Subject");
        request.Message.Body.Html.Data.ShouldBe(html);
        request.Message.Body.Text.Data.ShouldContain("Hello");
        request.Message.Body.Text.Data.ShouldContain("World");
        request.Message.Body.Text.Data.ShouldNotContain("<", Case.Sensitive);
    }

    [Fact]
    public void BuildRequest_PairsTheDisplayNameWithTheAddressInTheSource()
    {
        // Act
        var request = SesApiEmailSender.BuildRequest(
            "custom@example.com", "Custom Sender", "to@test.com", "Subject", "<p>Body</p>");

        // Assert -- SES takes one Source string, so the display name has to be folded into it.
        request.Source.ShouldBe("\"Custom Sender\" <custom@example.com>");
    }

    [Fact]
    public void BuildRequest_WithNoDisplayName_SendsTheBareAddressAsTheSource()
    {
        // Arrange -- Compass's AC-43 sender is address-only, and an address-only From must not
        // acquire an empty pair of quotes that SES would have to parse.

        // Act
        var request = SesApiEmailSender.BuildRequest(
            "no-reply@leadingedje.com", string.Empty, "to@test.com", "Subject", "<p>Body</p>");

        // Assert
        request.Source.ShouldBe("no-reply@leadingedje.com");
    }

    [Fact]
    public void BuildRequest_MarksEveryPartUtf8()
    {
        // Act
        var request = SesApiEmailSender.BuildRequest(
            "from@test.com", "Sender", "to@test.com", "Subject", "<p>Body</p>");

        // Assert -- SES defaults to 7-bit ASCII when no charset is given, which mangles a name or a
        // client with an accent in it.
        request.Message.Subject.Charset.ShouldBe("UTF-8");
        request.Message.Body.Html.Charset.ShouldBe("UTF-8");
        request.Message.Body.Text.Charset.ShouldBe("UTF-8");
    }

    // ------------------------------------------------------------------------- the From resolution

    [Fact]
    public void ResolveFrom_WithNoExplicitSender_UsesTheConfiguredDefault()
    {
        // Act -- a null sender means "use the configured default", so every existing caller is
        // unchanged by the transport swap.
        var (address, name) = SesApiEmailSender.ResolveFrom(null, DefaultSettings);

        // Assert
        address.ShouldBe("timesheet@leadingedje.com");
        name.ShouldBe("LeadingEDJE Timesheet");
    }

    [Fact]
    public void ResolveFrom_WithAnExplicitSender_UsesBothItsAddressAndName_NotTheConfiguredDefault()
    {
        // Arrange -- Compass's no-reply@ sender, address-only (AC-43). All-or-nothing: the empty
        // display name must override the configured Email:FromName rather than falling back to it.
        var from = new EmailFrom("no-reply@leadingedje.com");

        // Act
        var (address, name) = SesApiEmailSender.ResolveFrom(from, DefaultSettings);

        // Assert
        address.ShouldBe("no-reply@leadingedje.com");
        name.ShouldBe("", "the configured Timesheet FromName must not leak into an overridden sender");
    }

    // ------------------------------------------------------------------------- the settings read

    [Fact]
    public void ReadSettings_WithNoRegion_ReturnsNull()
    {
        // Arrange -- the local and PR-preview shape: the chart default is "" and no region is set.
        var config = Config(("Email:FromAddress", "leap@leadingedje.com"));

        // Act
        var settings = SesApiEmailSender.ReadSettings(config);

        // Assert
        settings.ShouldBeNull();
    }

    [Fact]
    public void ReadSettings_WithABlankRegion_ReturnsNull()
    {
        // Arrange -- Helm renders an unset value as "", not as an absent key, and a stray-space
        // value verbatim. Neither is a region.
        SesApiEmailSender.ReadSettings(Config(("Email:SesRegion", string.Empty))).ShouldBeNull();
        SesApiEmailSender.ReadSettings(Config(("Email:SesRegion", "   "))).ShouldBeNull();
    }

    [Fact]
    public void ReadSettings_CarriesTheRegionThrough()
    {
        // Act
        var settings = SesApiEmailSender.ReadSettings(Config(("Email:SesRegion", "us-east-1")));

        // Assert
        settings.ShouldNotBeNull();
        settings.Region.ShouldBe("us-east-1");
    }

    [Fact]
    public void ReadSettings_ReadsTheFromAddressAndName()
    {
        // Act
        var settings = SesApiEmailSender.ReadSettings(WithRegion(
            ("Email:FromAddress", "leap@leadingedje.com"), ("Email:FromName", "LEAP")));

        // Assert
        settings.ShouldNotBeNull();
        settings.FromAddress.ShouldBe("leap@leadingedje.com");
        settings.FromName.ShouldBe("LEAP");
    }

    [Fact]
    public void ReadSettings_WithNoFromConfigured_FallsBackToTheFrozenTimesheetSender()
    {
        // Act -- Email:FromAddress / Email:FromName are intentionally unset in every deploy file and
        // come from the code defaults.
        var settings = SesApiEmailSender.ReadSettings(WithRegion());

        // Assert
        settings.ShouldNotBeNull();
        settings.FromAddress.ShouldBe("timesheet@leadingedje.com");
        settings.FromName.ShouldBe("LeadingEDJE Timesheet");
    }

    // ------------------------------------------------------------------------- the client's region

    [Fact]
    public void CreateClient_UsesTheConfiguredRegion_NotTheAmbientOne()
    {
        // Arrange -- the trap this feature carries. The container's AWS_REGION is a different
        // region (values.yaml), so falling through to the SDK's default chain would route the send
        // there, and a region holding no verified identity refuses it for a reason that names
        // neither the region nor the identity.
        var settings = DefaultSettings with { Region = "us-east-1" };

        // Act
        using var client = SesApiEmailSender.CreateClient(settings);

        // Assert
        client.Config.RegionEndpoint.ShouldNotBeNull();
        client.Config.RegionEndpoint.SystemName.ShouldBe("us-east-1");
    }

    [Fact]
    public void CreateClient_HonoursAnyConfiguredRegion_SoTheKeyIsNotDecoration()
    {
        // Arrange -- a control for the test above: if the region were ignored and the client were
        // built from the ambient chain, both tests could not pass at once.
        var settings = DefaultSettings with { Region = "eu-west-2" };

        // Act
        using var client = SesApiEmailSender.CreateClient(settings);

        // Assert
        client.Config.RegionEndpoint.ShouldNotBeNull();
        client.Config.RegionEndpoint.SystemName.ShouldBe("eu-west-2");
    }

    // ------------------------------------------------------------------------------------ sending

    [Fact]
    public async Task SendAsync_SendsTheMessageThroughTheSesApi()
    {
        // Arrange
        using var ses = new FakeSesClient();
        var sender = Sender(ses, WithRegion());

        // Act
        await sender.SendAsync("kim.jones@leadingedje.com", "Timesheet Reminder", "<p>Please submit</p>");

        // Assert
        var request = ses.Requests.ShouldHaveSingleItem();
        request.Destination.ToAddresses.ShouldBe(["kim.jones@leadingedje.com"]);
        request.Message.Subject.Data.ShouldBe("Timesheet Reminder");
        request.Message.Body.Html.Data.ShouldBe("<p>Please submit</p>");
        request.Source.ShouldBe("\"LeadingEDJE Timesheet\" <timesheet@leadingedje.com>");
    }

    [Fact]
    public async Task SendAsync_WithAnExplicitSender_SendsAsThatSender()
    {
        // Arrange -- Compass's AC-43 coach notice.
        using var ses = new FakeSesClient();
        var sender = Sender(ses, WithRegion());

        // Act
        await sender.SendAsync(
            "coach@leadingedje.com", "Assignment Change", "Body",
            from: new EmailFrom("no-reply@leadingedje.com"));

        // Assert
        ses.Requests.ShouldHaveSingleItem().Source.ShouldBe("no-reply@leadingedje.com");
    }

    [Fact]
    public async Task SendAsync_WithNoRegionConfigured_SendsNothingAndDoesNotThrow()
    {
        // Arrange -- the no-op contract. It must stay a warning and a return rather than a throw:
        // NotificationRetryJob, CoachNotifier and OotoEmailService all reach this seam after their
        // write has committed, so a fault here would fail an action that already succeeded.
        using var ses = new FakeSesClient();
        var sender = Sender(ses, Config());

        // Act
        var send = async () =>
            await sender.SendAsync("to@leadingedje.com", "Subject", "<p>Body</p>");

        // Assert
        await send.ShouldNotThrowAsync();
        ses.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendAsync_WhenSesRefusesTheSend_TheFailureSurfaces()
    {
        // Arrange -- NotificationService and NotificationRetryJob are built on the send throwing: a
        // throw is how a notification gets recorded as failed and retried. An SES refusal -- an
        // unverified identity, or the role's ses:FromAddress condition -- has to surface that way
        // rather than be swallowed into a silent success.
        var refusal = new MessageRejectedException("Email address is not verified.");
        using var ses = new FakeSesClient(refusal);
        var sender = Sender(ses, WithRegion());

        // Act
        var send = async () =>
            await sender.SendAsync("to@leadingedje.com", "Subject", "<p>Body</p>");

        // Assert
        var thrown = await send.ShouldThrowAsync<MessageRejectedException>();
        thrown.ShouldBeSameAs(refusal);
    }

    /// <remarks>
    /// The name and the client string are real data (#598). Delivered as HTML, `&` becomes a broken
    /// entity, `<` swallows the rest of the line, and the newlines collapse -- in a notice that goes
    /// to a coach.
    /// </remarks>
    [Fact]
    public async Task SendAsync_WithAPlainTextBody_DeliversItVerbatimAsTextWithNoHtmlPart()
    {
        // Arrange
        using var ses = new FakeSesClient();
        var sender = Sender(ses, WithRegion());
        const string body =
            "Assignment ended for Bob & Kim <bob@leadingedje.com>\nClient: A < B Ltd\n";

        // Act
        await sender.SendAsync(
            "coach@leadingedje.com", "Assignment Change", body, EmailBodyFormat.PlainText);

        // Assert
        var request = ses.Requests.ShouldHaveSingleItem();
        request.Message.Body.Text.Data.ShouldBe(
            body,
            "a declared plain-text body must arrive byte-for-byte, entities and newlines intact");
        request.Message.Body.Html.ShouldBeNull(
            "an HTML part is what makes a mail client render the text as markup in the first place");
    }

    [Fact]
    public async Task SendAsync_WithAnHtmlBody_StillCarriesBothParts()
    {
        // Arrange -- the control: HTML must be untouched by the plain-text fix.
        using var ses = new FakeSesClient();
        var sender = Sender(ses, WithRegion());

        // Act
        await sender.SendAsync(
            "kim.jones@leadingedje.com", "Reminder", "<p>Please submit</p>", EmailBodyFormat.Html);

        // Assert
        var request = ses.Requests.ShouldHaveSingleItem();
        request.Message.Body.Html.Data.ShouldBe("<p>Please submit</p>");
        request.Message.Body.Text.Data.ShouldNotBeNullOrWhiteSpace(
            "the derived text alternative is what a text-only client falls back to");
    }
}
