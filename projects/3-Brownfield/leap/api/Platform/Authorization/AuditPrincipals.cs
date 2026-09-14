namespace LeadingEDJE.Leap.Api.Platform.Authorization;

/// <summary>
/// Named non-human principals written into an audit entry's actor fields.
/// </summary>
/// <remarks>
/// Bulk and migration writes must be attributed to a named system principal — never an empty actor,
/// a fabricated user, or whichever operator triggered the job. No production consumer today: the
/// Compass legacy-migration loader uses
/// <see cref="LeadingEDJE.Leap.Api.Platform.Auth.MigrationPrincipal"/>, which also authenticates an
/// HTTP request. Use that for a new bulk path. Adoption by the remaining Timesheet bulk paths versus
/// deletion is an open decision — do not settle it by deleting. Each value is deliberately not
/// parseable as a <see cref="System.Guid"/>; the converse does not hold, since some call sites pass a
/// free-form actor, so match these constants rather than testing whether a value parses.
/// </remarks>
public static class AuditPrincipals
{
    /// <summary>The principal for bulk and migration writes, including the legacy TPS load.</summary>
    /// <remarks>
    /// A validation-bypass path is reachable only by this principal and never through the application
    /// surface, or the exemption becomes a general-purpose way around validation.
    /// </remarks>
    public const string Migration = "system:migration";
}
