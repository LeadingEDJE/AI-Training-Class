namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// The single find-or-create core for <c>people</c> rows this application mints itself, shared by the
/// bootstrap SuperAdmin seeder and the Google SAML sign-in auto-create branch.
/// </summary>
/// <remarks>
/// It queries <c>people</c> directly, never through <c>ITpsClientService.GetEmployeeByEmailAsync</c>,
/// which collapses a null-<c>EdjeId</c> row to <c>null</c> and so cannot tell "no row" from
/// "deactivated row". An explicitly deactivated person is therefore never resurrected or duplicated.
/// </remarks>
public interface IPersonProvisioningService
{
    /// <summary>
    /// Finds the person row for <paramref name="email"/> (case-insensitive, matching the
    /// <c>LOWER(email)</c> index) or creates a minimal active row stamped with <paramref name="source"/>.
    /// </summary>
    /// <remarks>
    /// Never reactivates an inactive row, never inserts a duplicate, and never overwrites the provenance
    /// of a row it did not create.
    /// </remarks>
    /// <param name="email">The sign-in / configured email. Blank yields <see cref="PersonProvisionOutcome.Rejected"/>.</param>
    /// <param name="displayName">Optional display name (e.g. from the SAML assertion), split into first/last on create.</param>
    /// <param name="source">A <c>PersonSource</c> constant recorded on rows this call creates.</param>
    Task<PersonProvisionResult> EnsurePersonAsync(string? email, string? displayName, string source);

    /// <summary>
    /// Resolves the display name to stamp on a session and, where appropriate, persists it onto the
    /// person row so it also appears in Admin -> People.
    /// </summary>
    /// <remarks>
    /// Writes only when additive or when provenance allows: a row with no name takes
    /// <paramref name="assertionName"/> regardless of provenance, and a row the app minted (non-null
    /// <c>Source</c>, whose name may be an email-derived placeholder) yields to the authoritative
    /// assertion name. An HR-authoritative row (<c>Source</c> null) is never overwritten, and a blank
    /// <paramref name="assertionName"/> writes nothing — no name is fabricated here. Always returns a
    /// non-empty name for the claim: the stored name, else <paramref name="assertionName"/>, else
    /// <paramref name="resolvedDisplayName"/>. With no matching <c>people</c> row this is a strict no-op
    /// and the caller's directory lookup stays authoritative.
    /// </remarks>
    /// <param name="edjeId">The resolved person's internal key.</param>
    /// <param name="assertionName">The display name from the SAML assertion, if the IdP sent one.</param>
    /// <param name="resolvedDisplayName">
    /// The name the caller already resolved from the directory. Never blank in practice — the directory
    /// substitutes the email when a row carries no name — so it doubles as the last-resort claim value.
    /// </param>
    Task<string> EnsureDisplayNameAsync(Guid edjeId, string? assertionName, string resolvedDisplayName);

    /// <summary>
    /// Fills a person row's name from <paramref name="candidateName"/> only when the row currently has
    /// none. Never replaces an existing name, and never creates a row.
    /// </summary>
    /// <remarks>
    /// The seeder's counterpart to <see cref="EnsureDisplayNameAsync"/>, and deliberately weaker: a
    /// seeder-derived name is a guess from the email local part and the seeder re-runs on every pod
    /// start, so overwriting would clobber a real Google assertion name with that guess on every
    /// restart. <see cref="EnsureDisplayNameAsync"/> may replace an app-minted name because its input is
    /// authoritative; this may not. It is needed because <see cref="EnsurePersonAsync"/> writes names
    /// only on insert, so rows created by an earlier deploy — when the seeder passed no name — would
    /// otherwise stay blank forever.
    /// </remarks>
    Task TryFillMissingDisplayNameAsync(Guid edjeId, string? candidateName);
}

/// <summary>The disposition of an <see cref="IPersonProvisioningService.EnsurePersonAsync"/> call.</summary>
public enum PersonProvisionOutcome
{
    /// <summary>No row existed; a new active person row was created with the supplied provenance source.</summary>
    Created,

    /// <summary>An active row already existed and was reused; its provenance is left untouched.</summary>
    Existing,

    /// <summary>A row exists but is deactivated. Nothing was written, and the caller must deny.</summary>
    Inactive,

    /// <summary>The request was unusable (blank email). Nothing was written.</summary>
    Rejected,
}

/// <summary>
/// Outcome of a provisioning call, carrying enough resolved identity to stamp a session or grant a role
/// without a second round-trip. <see cref="EdjeId"/> is meaningful only for Created and Existing.
/// </summary>
public sealed record PersonProvisionResult(
    PersonProvisionOutcome Outcome,
    Guid EdjeId,
    string Email,
    string DisplayName)
{
    /// <summary>A newly created active person row.</summary>
    public static PersonProvisionResult ForCreated(Guid edjeId, string email, string displayName) =>
        new(PersonProvisionOutcome.Created, edjeId, email, displayName);

    /// <summary>An existing active person row, reused as-is.</summary>
    public static PersonProvisionResult ForExisting(Guid edjeId, string email, string displayName) =>
        new(PersonProvisionOutcome.Existing, edjeId, email, displayName);

    /// <summary>An existing deactivated person row — no EdjeId is surfaced and nothing was written.</summary>
    public static PersonProvisionResult ForInactive(string email) =>
        new(PersonProvisionOutcome.Inactive, Guid.Empty, email, string.Empty);

    /// <summary>An unusable request (blank email) — nothing was written.</summary>
    public static PersonProvisionResult ForRejected() =>
        new(PersonProvisionOutcome.Rejected, Guid.Empty, string.Empty, string.Empty);
}
