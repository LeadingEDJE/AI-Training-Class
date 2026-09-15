using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// The Compass application read surface's business layer (ADR-008).
/// </summary>
public interface ICompassDirectoryReadService
{
    /// <summary>Returns the Team Directory rows the caller is entitled to see (AC-5, AC-6, AC-9).</summary>
    /// <param name="query">Search, filters, and sort.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<TeamDirectoryRowDto>> GetTeamDirectoryAsync(
        TeamDirectoryQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the employee detail the caller is entitled to see, or <c>null</c> when they are not
    /// entitled to that record at all (AC-8, AC-10, AC-11).
    /// </summary>
    /// <param name="employeeId">The record being requested.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EmployeeDetailDto?> GetEmployeeDetailAsync(int employeeId, CancellationToken cancellationToken);

    /// <summary>Returns the Client Directory rows, each with its derived status (AC-12).</summary>
    /// <param name="query">Search and sort.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ClientDirectoryRowDto>> GetClientDirectoryAsync(
        ClientDirectoryQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns the client view the caller is entitled to see, or <c>null</c> when absent.</summary>
    /// <param name="clientId">The client being requested.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ClientViewDto?> GetClientViewAsync(int clientId, CancellationToken cancellationToken);
}
