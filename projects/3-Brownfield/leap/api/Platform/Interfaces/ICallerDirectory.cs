namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// Resolves the AUTHENTICATED CALLER to a directory employee (ADR-009). Platform-owned; the
/// implementation lives in whichever module owns the directory today.
/// </summary>
/// <remarks>
/// Answers "who is calling", not "what is that employee like" — the module's published contract
/// answers that, and is described rather than named because a file under <c>api/Platform/</c> may
/// not name a module type in ANY spelling (import, alias, qualification, <c>cref</c>);
/// <c>PlatformPurityTests</c> fails on all four, with no <c>Contracts</c> exemption. Hence a port
/// over BCL types only, asserted by <c>CallerDirectoryContractTests</c>. Nor is it
/// <see cref="IEmployeeDirectory"/>, keyed on an EDJE identity against the legacy directory; the
/// <c>CancellationToken</c> that port lacks is honoured here because this dependency is cancellable
/// end to end. No caching; resolving twice pays twice.
/// </remarks>
public interface ICallerDirectory
{
    /// <summary>
    /// Returns the directory employee the authenticated caller IS, or <c>null</c> when the caller
    /// has no counterpart in that directory.
    /// </summary>
    /// <remarks>
    /// No <c>email</c> parameter, deliberately: a parameterised port would let any caller resolve
    /// somebody else as "the caller", and that it cannot is what makes an answer trustworthy as an
    /// identity. Use the module's published contract to look somebody else up. Four outcomes, one of
    /// which throws — no caller at all (outside a request) PROPAGATES, because "there is no caller"
    /// must never become "no match"; an authenticated caller with no email claim returns
    /// <c>null</c> with a warning, and is reachable rather than defensive; no counterpart returns
    /// <c>null</c> and is normal; an inactive counterpart RESOLVES, since there is no active-only
    /// filter and whether an inactive EDJEr may act is an authorization question, not an identity one.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token. Honoured, not dropped.</param>
    Task<CallerDirectoryEntry?> ResolveCallerAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Who the caller is, as a directory employee: the directory's own key, a name to show, and the
/// address that identified them.
/// </summary>
/// <remarks>
/// Three members, each with a named consumer, and the count is the point: this is an identity, not
/// a profile. Timezone, active flag, coach and everything else the directory holds are deliberately
/// absent — all reachable from <paramref name="DirectoryId"/> through the module's published
/// contract in one call, at the tier the caller is entitled to. Copying them here would grow a
/// second employee DTO inside Platform, the duplication Principle II forbids deepening, and would
/// put Platform in the business of deciding which attributes an identity carries — a decision that
/// belongs to the module owning the data.
/// </remarks>
/// <param name="DirectoryId">
/// The directory's own integer key for this employee. This is the reference form a later cutover
/// (<c>#428</c>) will store in place of the legacy identity, so it is the member that has to be
/// here even before anything reads it.
/// </param>
/// <param name="DisplayName">
/// The caller's name, for display and for write attribution. Built by the owning module's shared
/// name rule, never re-derived from parts by a consumer.
/// </param>
/// <param name="Email">
/// The address this caller resolved on, as stored in the directory rather than as claimed.
/// It is the correlation key a transitional lookup against the frozen legacy row still needs; it
/// stops being load-bearing when <paramref name="DirectoryId"/> is what gets stored.
/// </param>
public sealed record CallerDirectoryEntry(int DirectoryId, string DisplayName, string Email);
