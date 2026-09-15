using LeadingEDJE.Leap.Api.Modules.Compass.Authorization;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>Data access for the Compass application read surface (ADR-008).</summary>
public interface ICompassReadRepository
{
    /// <summary>
    /// The Team Directory listing (AC-5, AC-6), already filtered, sorted, and scoped to the tier.
    /// </summary>
    /// <param name="query">Search, filters, and sort — the AC-6 controls.</param>
    /// <param name="tier">The viewer's tier, deciding which EDJErs are returned at all.</param>
    /// <param name="today">The business date, for BR-7's current-assignment test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<TeamDirectoryRowDto>> GetTeamDirectoryAsync(
        TeamDirectoryQuery query,
        CompassTier tier,
        DateOnly today,
        CancellationToken cancellationToken);

    /// <summary>
    /// The read-only employee detail (AC-8, AC-10, AC-11), or <c>null</c> when the viewer is not
    /// entitled to that record.
    /// </summary>
    /// <remarks>
    /// Returns a 403-equivalent forbidden marker rather than null when the viewer lacks entitlement,
    /// distinguishing that case from a genuinely missing record.
    /// </remarks>
    /// <param name="employeeId">The record being requested.</param>
    /// <param name="tier">The viewer's tier.</param>
    /// <param name="viewerEmail">The caller's email, for AC-11's own-record rule; null or blank owns nothing.</param>
    /// <param name="today">The business date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EmployeeDetailDto?> GetEmployeeDetailAsync(
        int employeeId,
        CompassTier tier,
        string? viewerEmail,
        DateOnly today,
        CancellationToken cancellationToken);

    /// <summary>
    /// The Client Directory listing (AC-12), with each client's derived status.
    /// </summary>
    /// <param name="query">Search and sort.</param>
    /// <param name="today">The business date, for the derivation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ClientDirectoryRowDto>> GetClientDirectoryAsync(
        ClientDirectoryQuery query,
        DateOnly today,
        CancellationToken cancellationToken);

    /// <summary>
    /// The client view (AC-13, AC-14, AC-15, AC-16), or <c>null</c> when no such client exists.
    /// </summary>
    /// <param name="clientId">The client being requested.</param>
    /// <param name="tier">The viewer's tier, deciding the panel set and the history's scope.</param>
    /// <param name="today">The business date, for the derived status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ClientViewDto?> GetClientViewAsync(
        int clientId,
        CompassTier tier,
        DateOnly today,
        CancellationToken cancellationToken);
}
