namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// Creates a contract period (SOW) under a client assignment.
/// </summary>
/// <remarks>
/// <c>HasPassedApplicationValidation</c> is deliberately not a field here. The service sets it:
/// <c>true</c> when the period satisfied both date rules on the way in, <c>false</c> for a
/// <c>LegacyMigrated</c> row admitted under the exemption. A caller-supplied value would let a row
/// assert a check it never passed, which is the one thing that flag exists to prevent.
/// <see cref="SowType"/> is restricted: <c>LegacyMigrated</c> is refused for every caller except the
/// migration principal (Principle VIII) — see <c>CompassSowMigrationService</c>.
/// </remarks>
/// <param name="ClientAssignmentId">The owning assignment. Must exist, or the write is not-found.</param>
/// <param name="SowType">
/// What this period represents. <c>LegacyMigrated</c> is migration-principal only.
/// </param>
/// <param name="RateIncrease">
/// Whether this period carries a rate increase. Permitted only on
/// <c>SowExtension</c> — a database CHECK enforces the same, and <c>LegacyMigrated</c> does not
/// inherit the permission because legacy TPS tracks no extensions at all. The rate itself is never
/// stored (AC-NFR-6).
/// </param>
/// <param name="SowStartDate">Required start date.</param>
/// <param name="SowEndDate">
/// Required end date, on or after <paramref name="SowStartDate"/> — except for a
/// <c>LegacyMigrated</c> period, which is admitted exactly as TPS recorded it.
/// </param>
/// <param name="Note">Optional free-text note. Elevated-visibility only.</param>
/// <param name="LegacyTpsId">
/// The identifier this record carried in the legacy TPS directory. Only the migration principal
/// may supply it (<see cref="CompassLegacyProvenance"/>); any other caller sending a value is
/// refused rather than having it quietly dropped. Absent for everything a person creates.
/// </param>
public record CompassSowRequest(
    int ClientAssignmentId,
    SowType SowType,
    bool RateIncrease,
    DateOnly SowStartDate,
    DateOnly SowEndDate,
    string? Note,
    string? LegacyTpsId = null
);

/// <summary>A contract period as returned by the configuration surface.</summary>
/// <param name="Id">The Compass-assigned identifier.</param>
/// <param name="ClientAssignmentId">The owning assignment.</param>
/// <param name="SowType">What this period represents.</param>
/// <param name="RateIncrease">Whether it carries a rate increase.</param>
/// <param name="HasPassedApplicationValidation">
/// Whether it satisfied the overlap and date-order rules. <c>false</c> on a migrated row until its
/// first edit through the application satisfies both in full.
/// </param>
/// <param name="SowStartDate">Start date.</param>
/// <param name="SowEndDate">End date.</param>
/// <param name="Note">Free-text note, or null.</param>
public record CompassSowDto(
    int Id,
    int ClientAssignmentId,
    SowType SowType,
    bool RateIncrease,
    bool HasPassedApplicationValidation,
    DateOnly SowStartDate,
    DateOnly SowEndDate,
    string? Note
);
