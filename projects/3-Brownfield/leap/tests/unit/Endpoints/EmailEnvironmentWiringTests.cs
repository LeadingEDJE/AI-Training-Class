using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Pins the DI wiring: which transport <c>Program.cs</c> picks, and whether it installs the
/// environment stamp in front of it.
/// </summary>
/// <remarks>
/// <c>EnvironmentStampingEmailSenderTests</c> proves the decorator behaves; it cannot prove
/// <c>Program.cs</c> installs it, and a conditional registration is precisely the shape where the
/// unit tests stay green while the feature is absent from every running process. The no-config case
/// matters as much as the configured one: <c>CoachNotificationTests.Outbox()</c> resolves
/// <see cref="IEmailSender"/> and asserts it is a <see cref="MockEmailSender"/>, so wrapping
/// unconditionally would break the Compass integration suite and add an indirection that does
/// nothing in production.
/// </remarks>
public class EmailEnvironmentWiringTests
{
    private sealed class ConfiguredFactory(params (string Key, string Value)[] settings)
        : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Email:SesRegion", string.Empty);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            base.ConfigureWebHost(builder);

            // Substitute the SES client so the tests below can assert the real transport was
            // selected without an AWS client in the graph. Registered after base so it wins; the fake
            // never reaches the network, and a test host has no credentials to reach it with.
            builder.ConfigureServices(services =>
            {
                services.RemoveAllOfType<IAmazonSimpleEmailService>();
                services.AddSingleton<IAmazonSimpleEmailService>(_ => new NeverSendingSesClient());
            });
        }
    }

    /// <summary>
    /// A SES client that refuses every send, so a wiring test cannot accidentally attempt one.
    /// </summary>
    private sealed class NeverSendingSesClient()
        : AmazonSimpleEmailServiceClient(Amazon.RegionEndpoint.USEast1)
    {
        public override Task<SendEmailResponse> SendEmailAsync(
            SendEmailRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "a wiring test must not send: it asserts which sender is registered, not delivery");
    }

    [Fact]
    public void WithNoEnvironmentLabelAndSendingEnabled_TheSenderIsNotWrapped()
    {
        // Arrange
        using var factory = new ConfiguredFactory();

        // Act
        var sender = factory.Services.GetRequiredService<IEmailSender>();

        // Assert
        sender.ShouldBeOfType<MockEmailSender>();
    }

    [Fact]
    public void WithAnEnvironmentLabel_TheSenderIsWrappedInTheStamp()
    {
        // Arrange
        using var factory = new ConfiguredFactory(("Email:EnvironmentLabel", "DEV"));

        // Act
        var sender = factory.Services.GetRequiredService<IEmailSender>();

        // Assert
        sender.ShouldBeOfType<EnvironmentStampingEmailSender>();
    }

    [Fact]
    public void WithSendingDisabled_TheSenderIsWrappedEvenWithNoLabel()
    {
        // Arrange — the kill switch lives in the same decorator, so "disabled" has to install it too.
        using var factory = new ConfiguredFactory(("Email:Enabled", "false"));

        // Act
        var sender = factory.Services.GetRequiredService<IEmailSender>();

        // Assert
        sender.ShouldBeOfType<EnvironmentStampingEmailSender>();
    }

    [Fact]
    public async Task WithSendingDisabled_NothingReachesTheUnderlyingSender()
    {
        // Arrange — the end-to-end shape of the requester's second ask, through the real container.
        using var factory = new ConfiguredFactory(("Email:Enabled", "false"));
        var sender = factory.Services.GetRequiredService<IEmailSender>();
        var inner = factory.Services.GetRequiredService<MockEmailSender>();

        // Act
        await sender.SendAsync("coach@leadingedje.com", "Assignment Change", "Body");

        // Assert
        inner.SentEmails.ShouldBeEmpty();
    }

    [Fact]
    public void WithARedirectAddress_TheSenderIsWrapped()
    {
        // Arrange — the redirect (issue #592) is the third reason to install the decorator, and it can
        // be configured on its own: an environment that sets a redirect and no label still has to be
        // rerouted.
        using var factory = new ConfiguredFactory(
            ("Email:RedirectAllTo", "leap-dev-sink@leadingedje.com"));

        // Act
        var sender = factory.Services.GetRequiredService<IEmailSender>();

        // Assert
        sender.ShouldBeOfType<EnvironmentStampingEmailSender>();
    }

    [Fact]
    public void WithNoSesRegion_TheTransportIsTheInMemoryOutbox()
    {
        // Arrange -- the transport selection, blank half: local dev and every PR preview set no
        // region, so there is no SES client and no real transport. A preview has no IAM role to
        // assume either, so a real SES call there could only fail.
        using var factory = new ConfiguredFactory();

        // Act & Assert
        factory.Services.GetService<SesApiEmailSender>().ShouldBeNull(
            "a blank Email:SesRegion must not register the SES transport at all");
        factory.Services.GetRequiredService<IEmailSender>().ShouldBeOfType<MockEmailSender>();
    }

    [Fact]
    public void WithASesRegion_TheTransportIsTheSesApiSender()
    {
        // Arrange -- the transport selection, configured half. All-or-nothing on purpose: one
        // non-blank key switches the real transport on.
        using var factory = new ConfiguredFactory(("Email:SesRegion", "us-east-1"));

        // Act & Assert — the concrete registration is what the decorator resolves, so it is the one
        // that proves the selection. IEmailSender itself is the stamp here (see the test below).
        factory.Services.GetService<SesApiEmailSender>().ShouldNotBeNull(
            "a non-blank Email:SesRegion must register the SES API transport");
        factory.Services.GetService<MockEmailSender>().ShouldBeNull(
            "the in-memory outbox must not also be registered -- EnvironmentStampingEmailSender's "
                + "fail-closed check asks whether the inner sender IS one");
    }

    [Fact]
    public void WithARealTransportInANonProductionEnvironment_TheSenderIsWrappedWithNoOtherConfig()
    {
        // Arrange -- the fail-closed suppression only works if the decorator is actually installed,
        // and a deployed dev that configures the SES transport and nothing else is exactly the case
        // it exists for.
        using var factory = new ConfiguredFactory(("Email:SesRegion", "us-east-1"));
        factory.Services.GetRequiredService<IHostEnvironment>().IsProduction().ShouldBeFalse(
            "this test host must be non-production for the condition under test to apply");

        // Act
        var sender = factory.Services.GetRequiredService<IEmailSender>();

        // Assert
        sender.ShouldBeOfType<EnvironmentStampingEmailSender>();
    }

    [Fact]
    public async Task WithARedirectAddress_TheDeliveredMailGoesToItAndNamesTheRealRecipient()
    {
        // Arrange
        using var factory = new ConfiguredFactory(
            ("Email:EnvironmentLabel", "DEV"),
            ("Email:RedirectAllTo", "leap-dev-sink@leadingedje.com"));
        var sender = factory.Services.GetRequiredService<IEmailSender>();
        var inner = factory.Services.GetRequiredService<MockEmailSender>();
        inner.SentEmails.Clear();

        // Act
        await sender.SendAsync("kim.jones@leadingedje.com", "Assignment Change", "A plain sentence.");

        // Assert
        var sent = inner.SentEmails.ShouldHaveSingleItem();
        sent.To.ShouldBe("leap-dev-sink@leadingedje.com");
        sent.Body.ShouldContain("kim.jones@leadingedje.com");
    }

    [Fact]
    public async Task WithAnEnvironmentLabel_TheDeliveredMailCarriesIt()
    {
        // Arrange
        using var factory = new ConfiguredFactory(("Email:EnvironmentLabel", "DEV"));
        var sender = factory.Services.GetRequiredService<IEmailSender>();
        var inner = factory.Services.GetRequiredService<MockEmailSender>();
        inner.SentEmails.Clear();

        // Act
        await sender.SendAsync("coach@leadingedje.com", "Assignment Change", "A plain sentence.");

        // Assert
        var sent = inner.SentEmails.ShouldHaveSingleItem();
        sent.Subject.ShouldBe("[DEV] Assignment Change");
        sent.Body.ShouldContain("DEV");
    }
}
