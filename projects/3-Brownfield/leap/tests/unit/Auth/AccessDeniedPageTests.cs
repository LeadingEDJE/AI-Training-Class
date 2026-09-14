using LeadingEDJE.Leap.Api.Platform.Auth;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Auth;

public class AccessDeniedPageTests
{
    [Theory]
    [InlineData(SignInDenialReasons.InactivePersonRecord)]
    [InlineData(SignInDenialReasons.NoMatchingPersonRecord)]
    [InlineData(SignInDenialReasons.SignInFailed)]
    [InlineData(null)]
    public void Render_IncludesTheCorrectSupportAddress_AndNeverTheOldOne(string? reason)
    {
        // Arrange & Act
        var html = AccessDeniedPage.Render(reason);

        // Assert
        html.ShouldContain("help@leadingedje.com");
        html.ShouldNotContain("helpdesk@leadingedje.com");
    }

    [Theory]
    [InlineData(SignInDenialReasons.UnauthorizedDomain)]
    [InlineData(SignInDenialReasons.InactivePersonRecord)]
    [InlineData(null)]
    public void Render_RetryLink_ForcesTheGoogleAccountChooser(string? reason)
    {
        // Arrange & Act
        var html = AccessDeniedPage.Render(reason);

        // Assert — the retry link forces Google's chooser so a wrong-account visitor can switch (#671).
        html.ShouldContain("/auth/login?switchAccount=true");
        html.ShouldContain("Sign in with a different account");
        html.ShouldNotContain("\"/auth/login\"");
    }
}
