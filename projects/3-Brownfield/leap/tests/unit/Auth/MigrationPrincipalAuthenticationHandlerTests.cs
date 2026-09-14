using System.Security.Claims;
using System.Text.Encodings.Web;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Auth;

/// <summary>
/// The handler that turns a bearer token into the migration principal.
/// </summary>
/// <remarks>
/// <para>
/// The sibling suite tests the OPTIONS and the PRINCIPAL; nothing tested the handler. That
/// left the actual authentication decision — the security boundary a deployed migration crosses —
/// exercised only end to end, where a wrong answer is easy to mistake for a configuration problem.
/// </para>
/// <para>
/// Every case here is a refusal except one. That ratio is the point: a handler that authenticates
/// too eagerly is the failure that matters, and the four <c>NoResult</c>/<c>Fail</c> paths are what
/// stop a request from becoming the Compass root by accident.
/// </para>
/// </remarks>
public class MigrationPrincipalAuthenticationHandlerTests
{
    private const string ConfiguredToken = "a-configured-migration-token";

    /// <summary>Builds an initialised handler over a request carrying <paramref name="header"/>.</summary>
    private static async Task<AuthenticateResult> AuthenticateAsync(string? token, string? header)
    {
        var handler = new MigrationPrincipalAuthenticationHandler(
            new OptionsMonitorStub(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            Options.Create(new MigrationPrincipalOptions { Token = token })
        );

        var context = new DefaultHttpContext();

        if (header is not null)
        {
            context.Request.Headers.Authorization = header;
        }

        await handler.InitializeAsync(
            new AuthenticationScheme(
                MigrationPrincipal.Scheme,
                displayName: null,
                typeof(MigrationPrincipalAuthenticationHandler)
            ),
            context
        );

        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task WhenNoTokenIsConfigured_ItAuthenticatesNobody()
    {
        // Act — ⚠️ defence in depth. Program.cs does not register the scheme when unconfigured, so
        // this is unreachable TODAY. It is a property of the composition root, not of this class,
        // and a future refactor that registers unconditionally must not start admitting people.
        var result = await AuthenticateAsync(token: null, header: $"Bearer {ConfiguredToken}");

        // Assert
        result.None.ShouldBeTrue();
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task WithNoAuthorizationHeader_ItDefersRatherThanFailing()
    {
        // Act — deferring matters: an ordinary browser request carries no bearer token, and a
        // FAILURE here would turn every cookie request into a 401 instead of falling through.
        var result = await AuthenticateAsync(ConfiguredToken, header: null);

        // Assert
        result.None.ShouldBeTrue();
    }

    [Fact]
    public async Task WithABlankAuthorizationHeader_ItDefers()
    {
        // Act
        var result = await AuthenticateAsync(ConfiguredToken, header: "   ");

        // Assert
        result.None.ShouldBeTrue();
    }

    [Fact]
    public async Task WithANonBearerScheme_ItDefers()
    {
        // Act — a Basic header is somebody else's business, not a failed migration attempt.
        var result = await AuthenticateAsync(ConfiguredToken, header: "Basic dXNlcjpwYXNz");

        // Assert
        result.None.ShouldBeTrue();
    }

    [Fact]
    public async Task WithTheWrongToken_ItFailsRatherThanDeferring()
    {
        // Act — ⚠️ the one case that must FAIL rather than defer. A presented-but-wrong token is an
        // attempt, and answering NoResult would let it fall through to another scheme and possibly
        // succeed as someone else.
        var result = await AuthenticateAsync(ConfiguredToken, header: "Bearer not-the-right-token");

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.None.ShouldBeFalse();
        result.Failure.ShouldNotBeNull();
    }

    [Fact]
    public async Task WithTheConfiguredToken_ItAuthenticatesTheMigrationPrincipal()
    {
        // Act
        var result = await AuthenticateAsync(ConfiguredToken, header: $"Bearer {ConfiguredToken}");

        // Assert
        result.Succeeded.ShouldBeTrue();
        MigrationPrincipal.IsMigrationPrincipal(result.Principal).ShouldBeTrue();
    }

    [Fact]
    public async Task TheAuthenticatedPrincipal_HoldsCompassRootAndNoTimesheetRole()
    {
        // Act
        var result = await AuthenticateAsync(ConfiguredToken, header: $"Bearer {ConfiguredToken}");

        // Assert — Principle IV names one direction that fails OPEN: a timesheet role string granted
        // here would silently grant it IN TIMESHEET. This is the assertion that keeps that shut.
        var privileges = result.Principal!
            .FindAll(AuthConstants.ClaimTypes.PrivilegeClaim)
            .Select(c => c.Value)
            .ToList();

        privileges.ShouldBe([RolePolicy.CompassSuperAdminRole]);
    }

    [Fact]
    public async Task ASurroundingWhitespaceToken_StillAuthenticates()
    {
        // Act — the handler trims. A token pasted into a shell with a trailing space is the ordinary
        // operator mistake, and refusing it would read as a wrong credential rather than a stray byte.
        var result = await AuthenticateAsync(ConfiguredToken, header: $"Bearer {ConfiguredToken}  ");

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    /// <summary>Minimal monitor — the handler reads nothing from the scheme options.</summary>
    private sealed class OptionsMonitorStub : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();

        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }
}
