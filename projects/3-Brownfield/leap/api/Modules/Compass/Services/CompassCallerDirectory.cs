using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Resolves the authenticated caller to a Compass employee — the Compass side of the Platform-owned
/// caller-resolution port.
/// </summary>
/// <remarks>
/// Filters out inactive EDJErs before resolving them, matching every other directory port's
/// active-only contract.
/// </remarks>
public class CompassCallerDirectory(
    ICompassDirectoryRepository repository,
    ICurrentUserContext currentUser,
    ILogger<CompassCallerDirectory> logger) : ICallerDirectory
{
    /// <inheritdoc />
    /// <remarks>
    /// Wrapped in a try/catch so a missing HTTP context resolves to a quiet no-match instead of
    /// propagating, keeping this port consistent with the other directory ports.
    /// </remarks>
    public async Task<CallerDirectoryEntry?> ResolveCallerAsync(CancellationToken cancellationToken)
    {
        var email = currentUser.Email;

        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning(
                "Caller {EdjeId} carries no email claim; cannot resolve a Compass directory record",
                currentUser.EdjeId);
            return null;
        }

        var employee = await repository.GetEmployeeByEmailAsync(email, cancellationToken);

        if (employee is null)
        {
            logger.LogWarning(
                "Caller {EdjeId} has no Compass employee for their session address; OOTO cannot "
                    + "resolve them until issue #428's remap covers this EDJEr",
                currentUser.EdjeId);
            return null;
        }

        // Returns the claim value directly for consistency with the caller's session token.
        return new CallerDirectoryEntry(
            employee.Id,
            CompassDisplayName.For(employee),
            employee.Email);
    }
}
