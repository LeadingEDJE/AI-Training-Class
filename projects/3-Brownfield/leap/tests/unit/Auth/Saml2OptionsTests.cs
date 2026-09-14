using LeadingEDJE.Leap.Api.Platform.Auth;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Auth;

/// <summary>
/// Pins the "Saml2" configuration-section binding contract that deployed environments
/// (Helm values, SSM/secret-delivered metadata XML) must match. Program.cs binds this
/// shape at startup; a rename here silently breaks SAML registration in every environment.
/// </summary>
public class Saml2OptionsTests
{
    [Fact]
    public void Bind_FullSaml2Section_PopulatesAllProperties()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Saml2:EntityId"] = "https://timesheet.example.com/Saml2",
                ["Saml2:GroupsAttributeName"] = "google-groups",
                ["Saml2:IdentityProvider:EntityId"] = "https://accounts.google.com/o/saml2?idpid=X",
                ["Saml2:IdentityProvider:MetadataLocation"] = "/etc/saml/idp-metadata.xml",
                ["Saml2:IdentityProvider:MetadataXml"] = "<EntityDescriptor/>",
            })
            .Build();

        // Act
        var options = new Saml2Options();
        configuration.GetSection("Saml2").Bind(options);

        // Assert
        options.EntityId.ShouldBe("https://timesheet.example.com/Saml2");
        options.GroupsAttributeName.ShouldBe("google-groups");
        options.IdentityProvider.EntityId.ShouldBe("https://accounts.google.com/o/saml2?idpid=X");
        options.IdentityProvider.MetadataLocation.ShouldBe("/etc/saml/idp-metadata.xml");
        options.IdentityProvider.MetadataXml.ShouldBe("<EntityDescriptor/>");
    }

    [Fact]
    public void Bind_EmptySection_KeepsSamlOffDefaults()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        // Act
        var options = new Saml2Options();
        configuration.GetSection("Saml2").Bind(options);

        // Assert — all-or-nothing registration posture relies on these defaults
        options.EntityId.ShouldBe(string.Empty);
        options.GroupsAttributeName.ShouldBe("groups");
        options.IdentityProvider.ShouldNotBeNull();
        options.IdentityProvider.EntityId.ShouldBe(string.Empty);
        options.IdentityProvider.MetadataLocation.ShouldBe(string.Empty);
        options.IdentityProvider.MetadataXml.ShouldBe(string.Empty);
    }
}
