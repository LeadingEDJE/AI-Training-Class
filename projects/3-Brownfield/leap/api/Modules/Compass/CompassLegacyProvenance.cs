using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Auth;

namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// Who may stamp a Compass record with the identifier it carried in the legacy TPS directory, and
/// what shape that identifier is stored in.
/// </summary>
public static class CompassLegacyProvenance
{
    /// <summary>Whether a caller may set the provenance value they supplied.</summary>
    /// <param name="user">The requesting principal.</param>
    /// <param name="legacyTpsId">The supplied identifier, if any.</param>
    /// <returns>
    /// <c>true</c> when the value is absent — the ordinary case, allowed for everyone — or when the
    /// caller is the migration principal itself.
    /// </returns>
    public static bool MaySet(ClaimsPrincipal? user, string? legacyTpsId) =>
        Normalise(legacyTpsId) is null || MigrationPrincipal.IsMigrationPrincipal(user);

    /// <summary>Reduces a supplied identifier to what should be stored.</summary>
    /// <param name="legacyTpsId">The supplied identifier, if any.</param>
    /// <returns>The trimmed identifier, or <c>null</c> when nothing meaningful was supplied.</returns>
    public static string? Normalise(string? legacyTpsId) =>
        string.IsNullOrWhiteSpace(legacyTpsId) ? null : legacyTpsId.Trim();

    /// <summary>The suffix every provenance unique index's name ends with.</summary>
    public const string ConstraintNameSuffix = "_legacy_tps_id";

    /// <summary>Whether a rejected write collided on a provenance index rather than a field one.</summary>
    /// <param name="exception">The translated duplicate-key failure.</param>
    /// <returns><c>true</c> when the violated index is a <c>legacy_tps_id</c> one.</returns>
    /// <remarks>
    /// Matched against an enumerated list of the six index names rather than by suffix, so every
    /// new migrated table must be added to the list by hand. A null constraint name answers
    /// <c>true</c> by design — see the provenance matching spec for the full list.
    /// </remarks>
    public static bool IsProvenanceCollision(CompassDuplicateKeyException exception) =>
        exception.ConstraintName?.EndsWith(ConstraintNameSuffix, StringComparison.Ordinal) == true;

    /// <summary>The message for a write that collided on a provenance index.</summary>
    /// <param name="legacyTpsId">The identifier the caller supplied.</param>
    /// <param name="subject">What kind of record it was recorded against, for the sentence.</param>
    /// <returns>A message naming the identifier and the record kind.</returns>
    public static string CollisionMessage(string? legacyTpsId, string subject) =>
        $"The legacy TPS identifier '{Normalise(legacyTpsId)}' is already recorded on another {subject}.";

    /// <summary>The response for a caller who supplied provenance and may not.</summary>
    /// <returns>A 403 naming the field and who may set it.</returns>
    public static IResult Refusal() =>
        Results.Problem(
            title: "Legacy provenance may only be set by the migration principal",
            detail: "This request supplied 'legacyTpsId'. That field records the identifier a record "
                + "carried in the legacy TPS directory, and only the migration principal may set it. "
                + "If this is a migration run, its token is not configured for this environment; if "
                + "it is not, omit the field.",
            statusCode: StatusCodes.Status403Forbidden
        );
}
