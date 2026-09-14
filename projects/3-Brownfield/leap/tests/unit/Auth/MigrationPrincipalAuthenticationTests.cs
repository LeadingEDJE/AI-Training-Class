using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Auth;

/// <summary>
/// The migration principal — the only non-browser identity that may write to Compass.
/// </summary>
/// <remarks>
/// <para>
/// Two constitution obligations meet in this type and both are asserted here. Principle VIII
/// requires bulk and migration writes to be attributed to a named system principal,
/// distinguishable from operator activity. Principle IV requires that a module's authority
/// grants nothing in another module — and names the fail-OPEN direction explicitly: anything that
/// resolves to one of timesheet's nine bare role strings grants that role in timesheet.
/// </para>
/// <para>
/// The scheme is config-gated: with no token configured it must not authenticate anything,
/// so a deployment that never sets one gains no new surface at all.
/// </para>
/// </remarks>
public class MigrationPrincipalAuthenticationTests
{
    private const string Token = "test-token-not-a-real-secret";

    [Fact]
    public void IsConfigured_IsFalse_WhenNoTokenIsSet()
    {
        // Arrange
        var options = new MigrationPrincipalOptions();

        // Act / Assert — an unset token means the scheme is never registered.
        options.IsConfigured.ShouldBeFalse();
    }

    [Fact]
    public void IsConfigured_IsFalse_ForAWhitespaceToken()
    {
        // Arrange — an empty string in configuration is a MISSING value, not a valid secret. If
        // whitespace counted as configured, `Auth__MigrationPrincipal__Token=""` would register a
        // scheme whose token nobody can guess but which also accepts nothing — a silent trap.
        var options = new MigrationPrincipalOptions { Token = "   " };

        // Act / Assert
        options.IsConfigured.ShouldBeFalse();
    }

    [Fact]
    public void IsConfigured_IsFalse_ForAnEmptyToken()
    {
        // Arrange — the EXACT value every deployed environment now receives when no migration is
        // planned. `secrets.Auth__MigrationPrincipal__Token` defaults to "" in the chart, so the
        // container gets the variable SET AND EMPTY rather than absent, and every PR preview lands
        // here. The sibling test above covers "   "; this covers the literal the chart emits.
        var options = new MigrationPrincipalOptions { Token = string.Empty };

        // Act / Assert — unconfigured, so the scheme is never registered and no bearer surface exists.
        options.IsConfigured.ShouldBeFalse();
    }

    [Fact]
    public void IsConfigured_IsTrue_WhenATokenIsSet()
    {
        // Arrange
        var options = new MigrationPrincipalOptions { Token = Token };

        // Act / Assert
        options.IsConfigured.ShouldBeTrue();
    }

    [Fact]
    public void Matches_RejectsAWrongToken()
    {
        // Arrange
        var options = new MigrationPrincipalOptions { Token = Token };

        // Act / Assert
        options.Matches("wrong").ShouldBeFalse();
        options.Matches(string.Empty).ShouldBeFalse();
        options.Matches(null).ShouldBeFalse();
    }

    [Fact]
    public void Matches_AcceptsTheConfiguredToken()
    {
        // Arrange
        var options = new MigrationPrincipalOptions { Token = Token };

        // Act / Assert
        options.Matches(Token).ShouldBeTrue();
    }

    [Fact]
    public void Matches_IsFalse_WhenUnconfigured_EvenForAnEmptyPresentedToken()
    {
        // Arrange — the degenerate case worth pinning: an unconfigured scheme must not accept ""
        // by string equality with its own unset token.
        var options = new MigrationPrincipalOptions();

        // Act / Assert
        options.Matches(string.Empty).ShouldBeFalse();
        options.Matches(null).ShouldBeFalse();
    }

    [Fact]
    public void CreatePrincipal_CarriesAStableEdjeId_SoAuditCanAttributeTheWrite()
    {
        // Act
        var principal = MigrationPrincipal.Create();

        // Assert — CurrentUserContext parses this claim as a Guid and the audit trail records it as
        // the actor. Without it every migration write throws on attribution.
        var edjeId = principal.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim);
        edjeId.ShouldNotBeNullOrWhiteSpace();
        Guid.TryParse(edjeId, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(MigrationPrincipal.EdjeId);
    }

    [Fact]
    public void CreatePrincipal_IsNamedDistinctlyFromAnyHuman()
    {
        // Act
        var principal = MigrationPrincipal.Create();

        // Assert — Principle VIII: "distinguishable from operator activity".
        principal.Identity!.Name.ShouldBe(MigrationPrincipal.Name);
        principal.Identity.Name.ShouldNotBeNullOrWhiteSpace();
        principal.Identity.IsAuthenticated.ShouldBeTrue();
    }

    [Fact]
    public void CreatePrincipal_GrantsCompassRootThroughAPrivilegeClaim()
    {
        // Act
        var principal = MigrationPrincipal.Create();

        // Assert — a Privilege claim is resolved by CompositeAuthorizationResolver BEFORE it falls
        // back to the user_roles table, so the principal needs no database row. That is deliberate:
        // a migration identity that depends on seeded data cannot bootstrap an empty environment.
        principal
            .HasClaim(AuthConstants.ClaimTypes.PrivilegeClaim, RolePolicy.CompassSuperAdminRole)
            .ShouldBeTrue();
    }

    [Fact]
    public void CreatePrincipal_GrantsNothingInTimesheetOrOoto()
    {
        // Arrange — the nine bare timesheet role strings. Principle IV names this as the one
        // direction that fails OPEN: any of these on a module principal grants that role IN
        // TIMESHEET, silently and with full authority.
        string[] timesheetRoles =
        [
            "EDJEr",
            "Manager",
            "TimesheetProcessor",
            "Accounting",
            "HR",
            "Ops",
            "PayrollProcessor",
            "Admin",
            "SuperAdmin",
        ];

        // Act
        var principal = MigrationPrincipal.Create();
        var granted = principal
            .FindAll(AuthConstants.ClaimTypes.PrivilegeClaim)
            .Select(c => c.Value)
            .ToArray();

        // Assert
        foreach (var role in timesheetRoles)
        {
            granted.ShouldNotContain(role, $"'{role}' would grant that role in timesheet.");
        }
    }

    [Fact]
    public void CreatePrincipal_IsTheOnlyIdentityThatMayWriteLegacyMigratedSows()
    {
        // Act / Assert — the SOW service gates the validation bypass on principal IDENTITY rather
        // than on a role name, so that granting someone the Compass root does not also hand them a
        // way past both partial SOW constraints (research R5). This asserts the identity test the
        // service will use.
        MigrationPrincipal.IsMigrationPrincipal(MigrationPrincipal.Create()).ShouldBeTrue();

        var human = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, Guid.NewGuid().ToString()),
                    new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, RolePolicy.CompassSuperAdminRole),
                ],
                "cookie"
            )
        );

        MigrationPrincipal.IsMigrationPrincipal(human).ShouldBeFalse();
    }

    [Fact]
    public void TriggeredBy_IsDistinctFromTheOperatorValue()
    {
        // Assert — Principle VIII. "Compass Admin" is what CompassEmployeeService stamps on an
        // operator write; a migration write must not be mistakable for one.
        MigrationPrincipal.AuditTriggeredBy.ShouldNotBe("Compass Admin");
        MigrationPrincipal.AuditTriggeredBy.ShouldNotBeNullOrWhiteSpace();
    }
}
