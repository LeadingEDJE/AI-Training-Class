using LeadingEDJE.Leap.Api.Platform.Services;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// The two-condition gate in front of the destructive developer-tools surface.
/// </summary>
/// <remarks>
/// <para>
/// These cases are the whole security argument for the feature, so they are enumerated rather
/// than sampled. Everything downstream — the endpoint mapping, the button, the modal — assumes
/// this function is right. It is a pure function precisely so every combination can be driven
/// without a host.
/// </para>
/// <para>
/// The environment names are the REAL ones this repository uses, not invented examples: local runs
/// <c>Development</c>, the unit suite runs <c>Testing</c>, deployed dev and every PR preview run
/// <c>Staging</c>, and only <c>values-production.yaml</c> sets <c>Production</c>. A test that only
/// covered Development and Production would miss the environment the feature was actually built for.
/// </para>
/// </remarks>
public class DeveloperToolsGateTests
{
    private static IConfiguration Configuration(string? enabled)
    {
        var values = new Dictionary<string, string?>();
        if (enabled is not null)
        {
            values[DeveloperToolsGate.EnabledKey] = enabled;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public void IsEnabled_FlagTrueAndNotProduction_IsEnabled(string environment)
    {
        // Arrange
        var configuration = Configuration("true");

        // Act
        var enabled = DeveloperToolsGate.IsEnabled(configuration, environment);

        // Assert
        enabled.ShouldBeTrue();
    }

    [Fact]
    public void IsEnabled_FlagTrueInProduction_IsDisabled()
    {
        // Arrange — the case the second condition exists for: configuration says yes, and the answer
        // is still no.
        var configuration = Configuration("true");

        // Act
        var enabled = DeveloperToolsGate.IsEnabled(configuration, "Production");

        // Assert
        enabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData("production")]
    [InlineData("PRODUCTION")]
    [InlineData("Production")]
    public void IsEnabled_ProductionNameIsMatchedCaseInsensitively(string environment)
    {
        // Arrange — ASPNETCORE_ENVIRONMENT is a free-text string set by hand in charts and workflows,
        // so a case-sensitive comparison would be one typo away from opening this in production.
        var configuration = Configuration("true");

        // Act
        var enabled = DeveloperToolsGate.IsEnabled(configuration, environment);

        // Assert
        enabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]      // key absent entirely — the default for every environment
    [InlineData("")]
    [InlineData("false")]
    [InlineData("yes")]     // truthy-looking to a human, unparseable to bool.TryParse
    [InlineData("1")]
    [InlineData("TRUE ")]   // trailing space; bool.TryParse trims, so this one IS true — see below
    public void IsEnabled_WithoutAnExplicitTrue_IsDisabled(string? flag)
    {
        // Arrange
        var configuration = Configuration(flag);

        // Act
        var enabled = DeveloperToolsGate.IsEnabled(configuration, "Development");

        // Assert — "TRUE " is the one entry here that IS enabled, because bool.TryParse trims
        // whitespace and ignores case. It is included so the boundary is written down rather than
        // discovered: only a value bool.TryParse reads as true opens this, and "yes"/"1" do not.
        enabled.ShouldBe(flag is "TRUE ");
    }

    [Fact]
    public void IsSuppressedByProduction_FlagTrueInProduction_IsTrue()
    {
        // Arrange — a real misconfiguration reaching a production host is the one state worth
        // logging loudly, so it is reported separately from "simply off".
        var configuration = Configuration("true");

        // Act
        var suppressed = DeveloperToolsGate.IsSuppressedByProduction(configuration, "Production");

        // Assert
        suppressed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("true", "Staging")]
    [InlineData("false", "Production")]
    [InlineData(null, "Production")]
    public void IsSuppressedByProduction_AnythingElse_IsFalse(string? flag, string environment)
    {
        // Arrange
        var configuration = Configuration(flag);

        // Act
        var suppressed = DeveloperToolsGate.IsSuppressedByProduction(configuration, environment);

        // Assert — an ordinary "off" must not be reported as a misconfiguration, or the critical log
        // it drives would fire on every production boot and mean nothing.
        suppressed.ShouldBeFalse();
    }
}
