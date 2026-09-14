using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Resolves the authenticated caller to a Compass employee — the Compass side of the Platform-owned
/// caller-resolution port.
/// </summary>
/// <remarks>
/// The first Platform contract this module supplies: the other directory ports answer from the frozen
/// legacy tables, this one from <c>compass.employee</c>, on email — the only attribute both stores hold
/// for the same person (severance-record § 1b, condition 3). It reaches the repository directly rather
/// than the module's published contract, whose email-keyed read resolves a viewer tier from
/// <see cref="ICurrentUserContext.Privileges"/> on a hit: nothing here is tier-gated, and a background
/// scope has no HTTP context to read it from. Normalisation stays the repository's, so the
/// <c>lower(btrim(email))</c> rule has one expression. No <c>IsActive</c> filter — whether an inactive
/// EDJEr may act is an authorization question, not an identity one.
/// </remarks>
public class CompassCallerDirectory(
    ICompassDirectoryRepository repository,
    ICurrentUserContext currentUser,
    ILogger<CompassCallerDirectory> logger) : ICallerDirectory
{
    /// <inheritdoc />
    /// <remarks>
    /// <see cref="ICurrentUserContext.Email"/> is read OUTSIDE any <c>try</c>, deliberately. The real
    /// context throws <c>InvalidOperationException("No HTTP context")</c> when no request is in
    /// flight, and that must propagate: for a port whose whole subject is the caller, "there is no
    /// caller" must never silently become "no match" — both are a <c>null</c> at the call site and
    /// only one is a bug. The blank-email branch logs the caller's EDJE identity and never the
    /// address, because the address is the caller-supplied value this branch exists to distrust.
    /// Nothing needs <c>LogSanitizer.Clean</c> as a result: a <c>Guid</c> cannot carry a newline.
    /// </remarks>
    public async Task<CallerDirectoryEntry?> ResolveCallerAsync(CancellationToken cancellationToken)
    {
        var email = currentUser.Email;

        if (string.IsNullOrWhiteSpace(email))
        {
            // Reachable, not defensive: CurrentUserContext.Email answers string.Empty when the claim
            // is missing, and the migration principal mints no email claim at all.
            logger.LogWarning(
                "Caller {EdjeId} carries no email claim; cannot resolve a Compass directory record",
                currentUser.EdjeId);
            return null;
        }

        var employee = await repository.GetEmployeeByEmailAsync(email, cancellationToken);

        if (employee is null)
        {
            // Logged, not silent: this is the UNMATCHED population the TPS-to-Compass ETL creates,
            // because it synthesises addresses and the delivery carries no email column
            // (severance-record § 1b). The remap has to drive that set to zero and can only do so if
            // the set is observable. The caller's EdjeId, never their address, which is the
            // untrusted value.
            logger.LogWarning(
                "Caller {EdjeId} has no Compass employee for their session address; OOTO cannot "
                    + "resolve them until issue #428's remap covers this EDJEr",
                currentUser.EdjeId);
            return null;
        }

        // employee.Email, not the claim: the stored address is the one a consumer correlates on, and
        // the two can differ in case or surrounding whitespace and still be the same person.
        return new CallerDirectoryEntry(
            employee.Id,
            CompassDisplayName.For(employee),
            employee.Email);
    }
}
