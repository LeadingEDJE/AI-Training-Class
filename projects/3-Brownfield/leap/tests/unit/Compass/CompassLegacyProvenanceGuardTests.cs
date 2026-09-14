using System.Security.Claims;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// Who may stamp a Compass record with a legacy TPS identifier.
/// </summary>
/// <remarks>
/// <para>
/// Gated on principal IDENTITY, not on a role — the same choice, for the same reason, as the
/// <c>LegacyMigrated</c> SOW bypass. The migration principal holds the Compass root role, so a
/// role-based check would let every Compass Super Admin sitting at a browser claim that a record they
/// just typed came out of TPS.
/// </para>
/// <para>
/// Why provenance needs a gate at all, given it is "just a string". It is unique per table and
/// it is what a crosswalk rebuild reads back. A hand-created record carrying a fabricated TPS id both
/// occupies an identifier the real migration needs — the load would then fail on a uniqueness
/// violation it cannot explain — and answers "where did this come from?" with a lie that outlives
/// everyone involved.
/// </para>
/// <para>
/// Refusal, never silent discard. Dropping the field for an unauthorised caller would return
/// <c>201 Created</c> to someone who believes they recorded provenance. That is the same failure mode
/// <c>JsonUnmappedMemberHandling.Disallow</c> exists to prevent on these very request types, and
/// <see cref="CompassClientRequest"/>'s remarks call silently ignoring a member "the worst of the
/// three possible outcomes".
/// </para>
/// </remarks>
public class CompassLegacyProvenanceGuardTests
{
    private static ClaimsPrincipal Human(string role = RolePolicy.CompassSuperAdminRole) =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, Guid.NewGuid().ToString()),
                    new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, role),
                ],
                authenticationType: "Test"
            )
        );

    [Fact]
    public void MigrationPrincipal_MaySetProvenance()
    {
        // Act
        var allowed = CompassLegacyProvenance.MaySet(MigrationPrincipal.Create(), "tps-employee-1001");

        // Assert
        allowed.ShouldBeTrue("the migration principal is the one caller that knows a TPS identifier");
    }

    [Fact]
    public void CompassSuperAdmin_MayNotSetProvenance()
    {
        // Act — ⚠️ the assertion that makes this a gate rather than a comment. A Super Admin holds the
        // same Compass root role the migration principal does, so anything keyed on the ROLE passes
        // here and the gate is gone.
        var allowed = CompassLegacyProvenance.MaySet(Human(), "tps-employee-1001");

        // Assert
        allowed.ShouldBeFalse("provenance is gated on identity, not on the Compass root role");
    }

    [Fact]
    public void AnyCaller_MayOmitProvenance()
    {
        // Act — the ordinary path. Every record a person creates has no TPS origin, so a null must
        // pass for everyone or the guard breaks all of Compass rather than guarding one field.
        var human = CompassLegacyProvenance.MaySet(Human(), legacyTpsId: null);
        var migration = CompassLegacyProvenance.MaySet(MigrationPrincipal.Create(), legacyTpsId: null);

        // Assert
        human.ShouldBeTrue();
        migration.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankProvenance_CountsAsOmitted(string blank)
    {
        // Act — an empty string is a MISSING value, not a claim to have come from TPS. Treating it as
        // present would refuse an ordinary caller whose client serialised "" for an absent field, and
        // storing it would put a non-null, non-meaningful value under the unique index — where the
        // SECOND such record would collide for no reason a reader could diagnose.
        var allowed = CompassLegacyProvenance.MaySet(Human(), blank);

        // Assert
        allowed.ShouldBeTrue("blank is absent, and absent is allowed for everyone");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalise_TurnsAbsentIntoNull(string? absent)
    {
        // Act
        var normalised = CompassLegacyProvenance.Normalise(absent);

        // Assert — so the column holds either a real identifier or NULL, never a blank string that
        // the unique index would treat as a value.
        normalised.ShouldBeNull();
    }

    [Fact]
    public void Normalise_TrimsARealIdentifier()
    {
        // Act
        var normalised = CompassLegacyProvenance.Normalise("  tps-client-3001  ");

        // Assert
        normalised.ShouldBe("tps-client-3001");
    }

    /// <summary>
    /// The refusal is a 403 that names the field and the likeliest cause.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 403 rather than 400, asserted exactly: the request is well-formed and the field is
    /// real — what is missing is the authority to use it. A 400 sends the caller hunting for a
    /// malformed body, which is the wrong place and the expensive kind of wrong.
    /// </para>
    /// <para>
    /// The detail must name <c>legacyTpsId</c>. The caller most likely to reach this is a
    /// migration run pointed at an environment where its token is not configured; without the field
    /// name the failure reads as a generic permissions problem on the whole create, and the actual
    /// fix — configure <c>Auth:MigrationPrincipal:Token</c> — is nowhere in view.
    /// </para>
    /// <para>
    /// Covered here rather than only through the API, because the integration suite that exercises
    /// this path is not part of the per-file coverage gate — which runs the unit suite alone.
    /// </para>
    /// </remarks>
    [Fact]
    public void Refusal_Is403_AndNamesTheField()
    {
        // Act
        var refusal = CompassLegacyProvenance.Refusal();

        // Assert
        var problem = refusal.ShouldBeOfType<ProblemHttpResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Title.ShouldBe(
            "Legacy provenance may only be set by the migration principal"
        );
        problem.ProblemDetails.Detail.ShouldNotBeNull();
        problem.ProblemDetails.Detail.ShouldContain("legacyTpsId");
    }
}
