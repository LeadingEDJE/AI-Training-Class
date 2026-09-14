namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;

/// <summary>
/// <c>POST /api/compass/assignments/{assignmentId}/sows</c>. <c>contracts/sow-write-surface.md</c> §2.
/// </summary>
/// <remarks>
/// <see cref="SowType.LegacyMigrated"/> must be rejected on input (FR-016). It is reserved for the
/// migration principal's own load path, and the service refuses it regardless of caller. There is no
/// <c>HasPassedApplicationValidation</c> field: the service sets it, <c>true</c> when both date rules
/// are satisfied on the way in, which is always so for a request this record can express, since
/// <see cref="SowType.LegacyMigrated"/> — the only type ever admitted with a value that would make it
/// <c>false</c> — is refused before it reaches the write path.
/// </remarks>
/// <param name="SowType">
/// What this period represents. <see cref="SowType.LegacyMigrated"/> is refused on input (FR-016).
/// </param>
/// <param name="RateIncrease">
/// Whether this period carries a rate increase. Permitted only on
/// <see cref="SowType.SowExtension"/> (FR-017, AC-35) — the rate itself is never stored (AC-NFR-6).
/// </param>
/// <param name="SowStartDate">Required start date.</param>
/// <param name="SowEndDate">Required end date, on or after <paramref name="SowStartDate"/> (FR-020).</param>
/// <param name="Note">Optional free-text note. Elevated-visibility only on read.</param>
public sealed record CreateSowRequest(
    SowType SowType,
    bool RateIncrease,
    DateOnly SowStartDate,
    DateOnly SowEndDate,
    string? Note);

/// <summary>
/// <c>PUT /api/compass/assignments/{assignmentId}/sows/{sowId}</c>. §2. Same shape as
/// <see cref="CreateSowRequest"/>; <see cref="SowType.LegacyMigrated"/> is refused here too (FR-016).
/// </summary>
public sealed record UpdateSowRequest(
    SowType SowType,
    bool RateIncrease,
    DateOnly SowStartDate,
    DateOnly SowEndDate,
    string? Note);
