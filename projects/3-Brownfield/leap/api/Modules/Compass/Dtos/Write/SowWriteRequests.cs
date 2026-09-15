namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;

/// <summary>
/// <c>POST /api/compass/assignments/{assignmentId}/sows</c>. <c>contracts/sow-write-surface.md</c> §2.
/// </summary>
/// <remarks>
/// <see cref="SowType.LegacyMigrated"/> is accepted on input like any other type; the migration
/// principal check that used to gate it was removed when the load path was retired.
/// </remarks>
/// <param name="SowType">What this period represents.</param>
/// <param name="RateIncrease">
/// Whether this period carries a rate increase. Restricted to periods other than
/// <see cref="SowType.SowExtension"/>, and the rate amount itself is stored alongside this flag.
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

/// <summary><c>PUT /api/compass/assignments/{assignmentId}/sows/{sowId}</c>.</summary>
public sealed record UpdateSowRequest(
    SowType SowType,
    bool RateIncrease,
    DateOnly SowStartDate,
    DateOnly SowEndDate,
    string? Note);
