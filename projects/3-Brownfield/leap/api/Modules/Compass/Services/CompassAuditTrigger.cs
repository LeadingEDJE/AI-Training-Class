using LeadingEDJE.Leap.Api.Platform.Auth;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Resolves the <c>TriggeredBy</c> value recorded against a Compass write.
/// </summary>
public static class CompassAuditTrigger
{
    /// <summary>
    /// The value recorded for a write made by a human through a Compass configuration surface.
    /// </summary>
    /// <remarks>
    /// Safe to change at any time; nothing downstream keys off this string.
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
