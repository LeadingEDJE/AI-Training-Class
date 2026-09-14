using LeadingEDJE.Leap.Api.Platform.Auth;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Resolves the <c>TriggeredBy</c> value recorded against a Compass write.
/// </summary>
/// <remarks>
/// Principle VIII requires bulk and migration writes to be attributed to a named system principal
/// distinguishable from operator activity. The migration principal holds the Compass root role, so it
/// is indistinguishable from a Super Admin by authority; the distinction is therefore drawn on the
/// actor's identifier and not on a role. That is the same reasoning as
/// <see cref="MigrationPrincipal.IsMigrationPrincipal"/>, which gates the SOW validation bypass:
/// role-based checks answer "what may this caller do", and the question here is "who is this caller".
/// </remarks>
public static class CompassAuditTrigger
{
    /// <summary>
    /// The value recorded for a write made by a human through a Compass configuration surface.
    /// </summary>
    /// <remarks>
    /// This is the pre-existing wire value, pinned by a test. Changing it would silently reclassify
    /// every historical Compass audit row for any reader grouping by <c>TriggeredBy</c>.
    /// </remarks>
    public const string Operator = "Compass Admin";

    /// <summary>Resolves the trigger value for an actor.</summary>
    /// <param name="actorEdjeId">The acting identity's stable identifier.</param>
    /// <returns>
    /// <see cref="MigrationPrincipal.AuditTriggeredBy"/> for the migration principal; otherwise
    /// <see cref="Operator"/>.
    /// </returns>
    public static string For(Guid actorEdjeId) =>
        actorEdjeId == MigrationPrincipal.EdjeId ? MigrationPrincipal.AuditTriggeredBy : Operator;
}
