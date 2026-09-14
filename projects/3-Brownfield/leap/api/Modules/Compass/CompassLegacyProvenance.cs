using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Auth;

namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// Who may stamp a Compass record with the identifier it carried in the legacy TPS directory, and
/// what shape that identifier is stored in.
/// </summary>
/// <remarks>
/// Gated on principal identity, not on a role, as the <c>LegacyMigrated</c> SOW bypass in
/// <c>CompassSowMigrationService</c> is: the migration principal holds the Compass root role, so a
/// role-based check would let any Compass Super Admin at a browser declare that a record they just
/// typed came out of TPS. <c>legacy_tps_id</c> is unique per table and is what a crosswalk rebuild
/// reads back, so a fabricated value occupies an identifier the real migration needs.
///
/// Refuse, never silently discard: dropping the field would hand back <c>201 Created</c> to a caller
/// who believes they recorded provenance. It is an authorization question, so it lives at the
/// endpoint boundary where the principal is known, answered once rather than threaded into validation.
/// </remarks>
public static class CompassLegacyProvenance
{
    /// <summary>Whether a caller may set the provenance value they supplied.</summary>
    /// <param name="user">The requesting principal.</param>
    /// <param name="legacyTpsId">The supplied identifier, if any.</param>
    /// <returns>
    /// <c>true</c> when the value is absent — the ordinary case, allowed for everyone — or when the
    /// caller is the migration principal itself.
    /// </returns>
    /// <remarks>
    /// The absent case is checked first and on its own, because it is the path every ordinary Compass
    /// write takes. A guard that answered the identity question first would still be correct, but this
    /// ordering makes it obvious that the gate cannot affect a caller who never mentions provenance.
    /// </remarks>
    public static bool MaySet(ClaimsPrincipal? user, string? legacyTpsId) =>
        Normalise(legacyTpsId) is null || MigrationPrincipal.IsMigrationPrincipal(user);

    /// <summary>Reduces a supplied identifier to what should be stored.</summary>
    /// <param name="legacyTpsId">The supplied identifier, if any.</param>
    /// <returns>The trimmed identifier, or <c>null</c> when nothing meaningful was supplied.</returns>
    /// <remarks>
    /// Blank collapses to null, and that matters more than it looks. The column is under a
    /// unique index, and Postgres treats nulls as distinct but empty strings as equal — so storing
    /// <c>""</c> would let the first such record through and collide on the second, for a reason no
    /// reader could diagnose from either row. Treating blank as absent also stops a client that
    /// serialises <c>""</c> for a missing field from being refused by <see cref="MaySet"/>.
    /// </remarks>
    public static string? Normalise(string? legacyTpsId) =>
        string.IsNullOrWhiteSpace(legacyTpsId) ? null : legacyTpsId.Trim();

    /// <summary>The suffix every provenance unique index's name ends with.</summary>
    public const string ConstraintNameSuffix = "_legacy_tps_id";

    /// <summary>Whether a rejected write collided on a provenance index rather than a field one.</summary>
    /// <param name="exception">The translated duplicate-key failure.</param>
    /// <returns><c>true</c> when the violated index is a <c>legacy_tps_id</c> one.</returns>
    /// <remarks>
    /// Matched by suffix rather than against an enumerated list of the four index names
    /// (<c>ux_employee_legacy_tps_id</c> and its siblings). The naming convention is
    /// <c>ux_&lt;table&gt;_legacy_tps_id</c> throughout, and a list would have to grow with every new
    /// migrated table, silently reporting the wrong field for the one nobody remembered to add; a
    /// suffix match fails safe instead. A null constraint name answers <c>false</c>: the caller keeps
    /// its existing message, which is still a 409 and still better than a 500.
    /// </remarks>
    public static bool IsProvenanceCollision(CompassDuplicateKeyException exception) =>
        exception.ConstraintName?.EndsWith(ConstraintNameSuffix, StringComparison.Ordinal) == true;

    /// <summary>The message for a write that collided on a provenance index.</summary>
    /// <param name="legacyTpsId">The identifier the caller supplied.</param>
    /// <param name="subject">What kind of record it was recorded against, for the sentence.</param>
    /// <returns>A message naming the identifier and the record kind.</returns>
    /// <remarks>
    /// Names the value, because the caller is almost always a migration run working through a batch
    /// and the useful question is which identifier already exists, not that one of them does.
    /// </remarks>
    public static string CollisionMessage(string? legacyTpsId, string subject) =>
        $"The legacy TPS identifier '{Normalise(legacyTpsId)}' is already recorded on another {subject}.";

    /// <summary>The response for a caller who supplied provenance and may not.</summary>
    /// <returns>A 403 naming the field and who may set it.</returns>
    /// <remarks>
    /// 403 rather than 400: the request is well-formed and the field is real — what is missing is the
    /// authority to use it, and a 400 would send the caller looking for a malformed body. The detail
    /// names the field explicitly, because the caller most likely to hit this is a script pointed at
    /// an environment where its migration token is not configured, and without the field name that
    /// reads as a generic permissions problem on the whole create.
    /// </remarks>
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
