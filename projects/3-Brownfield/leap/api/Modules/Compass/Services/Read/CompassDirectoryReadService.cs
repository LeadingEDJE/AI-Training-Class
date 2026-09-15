using LeadingEDJE.Leap.Api.Modules.Compass.Authorization;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>Builds Compass read projections for the caller's tier.</summary>
public class CompassDirectoryReadService(
    ICompassReadRepository repository,
    ICurrentUserContext currentUser,
    ICompassBusinessDate businessDate) : ICompassDirectoryReadService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<TeamDirectoryRowDto>> GetTeamDirectoryAsync(
        TeamDirectoryQuery query,
        CancellationToken cancellationToken) =>
        repository.GetTeamDirectoryAsync(
            query,
            CompassViewerTier.Resolve(currentUser.Privileges),
            businessDate.Today(),
            cancellationToken);

    /// <inheritdoc />
    public Task<EmployeeDetailDto?> GetEmployeeDetailAsync(
        int employeeId,
        CancellationToken cancellationToken) =>
        repository.GetEmployeeDetailAsync(
            employeeId,
            CompassViewerTier.Resolve(currentUser.Privileges),
            currentUser.Email,
            businessDate.Today(),
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ClientDirectoryRowDto>> GetClientDirectoryAsync(
        ClientDirectoryQuery query,
        CancellationToken cancellationToken) =>
        // No tier: client directory tiering was removed under AC-19 once panels replaced columns.
        repository.GetClientDirectoryAsync(query, businessDate.Today(), cancellationToken);

    /// <inheritdoc />
    public Task<ClientViewDto?> GetClientViewAsync(int clientId, CancellationToken cancellationToken) =>
        repository.GetClientViewAsync(
            clientId,
            CompassViewerTier.Resolve(currentUser.Privileges),
            businessDate.Today(),
            cancellationToken);
}
