using LeadingEDJE.Leap.Api.Modules.Compass.Authorization;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Implements the Compass directory boundary: projects a directory record to the module's DTOs,
/// and applies whatever updates the caller requests.
/// </summary>
public class CompassDirectoryService(
    ICompassDirectoryRepository repository,
    ICurrentUserContext currentUser) : IDirectory
{
    /// <inheritdoc />
    public async Task<CompassEmployeeDto?> GetEmployeeAsync(
        int employeeId, CancellationToken cancellationToken)
    {
        var employee = await repository.GetEmployeeAsync(employeeId, cancellationToken);
        if (employee is null)
        {
            return null;
        }

        return ToDto(employee, CompassViewerTier.Resolve(currentUser.Privileges));
    }

    /// <inheritdoc />
    public async Task<CompassEmployeeDto?> GetEmployeeByEmailAsync(
        string? email, CancellationToken cancellationToken)
    {
        var employee = await repository.GetEmployeeByEmailAsync(email, cancellationToken);

        if (employee is null)
        {
            return null;
        }

        return ToDto(employee, CompassViewerTier.Resolve(currentUser.Privileges));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompassEmployeeDto>> GetEmployeesByEmailAsync(
        IReadOnlyCollection<string?> emails, CancellationToken cancellationToken)
    {
        var employees = await repository.GetEmployeesByEmailAsync(emails, cancellationToken);
        if (employees.Count == 0)
        {
            return [];
        }

        var tier = CompassViewerTier.Resolve(currentUser.Privileges);

        return [.. employees.Select(employee => ToDto(employee, tier))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompassEmployeeDto>> GetEmployeesAsync(
        CancellationToken cancellationToken)
    {
        var employees = await repository.GetActiveEmployeesAsync(cancellationToken);
        if (employees.Count == 0)
        {
            return [];
        }

        var tier = CompassViewerTier.Resolve(currentUser.Privileges);

        return [.. employees.Select(employee => ToDto(employee, tier))];
    }

    /// <summary>
    /// Projects one Compass employee to the published contract, gating every field on the given
    /// viewer tier per the access matrix in docs/compass-directory-access.md.
    /// </summary>
    private static CompassEmployeeDto ToDto(Employee employee, CompassTier tier)
        => new()
        {
            Id = employee.Id,
            DisplayName = CompassDisplayName.For(employee),
            IsActive = employee.IsActive,
            Email = employee.Email,
            HireDate = employee.HireDate,
            EmployeeType = employee.EmployeeType?.TypeName ?? string.Empty,
            StateOfResidence = employee.StateOfResidence,
            Timezone = employee.Timezone,
            IsDeliveryTeam = employee.IsDeliveryTeam,
            Coach = employee.Coach is { } coach
                ? new CompassEmployeeCoachDto { Id = coach.Id, DisplayName = CompassDisplayName.For(coach) }
                : null,
            TimeTracking = tier.SeesTimeTrackingSettings()
                ? new CompassEmployeeTimeTrackingDto
                {
                    TimesheetRequired = employee.TimesheetRequired,
                    CanSubmitUnder40 = employee.CanSubmitUnder40,
                    IncludeInPayroll = employee.IncludeInPayroll,
                }
                : null,
        };

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompassInvoiceFrequencyDto>> GetInvoiceFrequenciesAsync(
        CancellationToken cancellationToken)
    {
        var types = await repository.GetInvoiceFrequenciesAsync(cancellationToken);

        return [.. types.Select(t => new CompassInvoiceFrequencyDto { Id = t.Id, TypeName = t.TypeName })];
    }

    /// <inheritdoc />
    public async Task<CompassClientDto?> GetClientAsync(
        int clientId, CancellationToken cancellationToken)
    {
        var result = await repository.GetClientAsync(clientId, cancellationToken);
        if (result is not { } found)
        {
            return null;
        }

        var (client, status) = found;

        return ToDto(client, status);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompassClientDto>> GetClientsAsync(
        CancellationToken cancellationToken)
    {
        var clients = await repository.GetClientsAsync(cancellationToken);

        return [.. clients.Select(row => ToDto(row.Client, row.Status))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompassBillableCategoryDto>> GetBillableCategoriesAsync(
        int clientId, CancellationToken cancellationToken)
    {
        var categories = await repository.GetBillableCategoriesAsync(clientId, cancellationToken);

        return [.. categories.Select(c => new CompassBillableCategoryDto
        {
            Id = c.Id,
            ClientId = c.ClientId,
            CategoryName = c.CategoryName,
            IsActive = c.IsActive,
        })];
    }

    /// <inheritdoc />
    public async Task<CompassAssignmentDto?> GetAssignmentAsync(
        int assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await repository.GetAssignmentAsync(assignmentId, cancellationToken);
        return assignment is null ? null : ToDto(assignment);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompassAssignmentDto>> GetAssignmentsByEmployeeAsync(
        int employeeId, CancellationToken cancellationToken)
    {
        var assignments = await repository.GetAssignmentsByEmployeeAsync(employeeId, cancellationToken);
        return [.. assignments.Select(ToDto)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompassAssignmentDto>> GetAssignmentsByClientAsync(
        int clientId, CancellationToken cancellationToken)
    {
        var assignments = await repository.GetAssignmentsByClientAsync(clientId, cancellationToken);
        return [.. assignments.Select(ToDto)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompassDirectorySowDto>> GetSowsByAssignmentAsync(
        int clientAssignmentId, CancellationToken cancellationToken)
    {
        var sows = await repository.GetSowsByAssignmentAsync(clientAssignmentId, cancellationToken);
        var tier = CompassViewerTier.Resolve(currentUser.Privileges);
        var seesRateIncreaseAndNotes = tier.SeesOthersSowsAndNotes();

        return [.. sows.Select(sow => new CompassDirectorySowDto
        {
            Id = sow.Id,
            ClientAssignmentId = sow.ClientAssignmentId,
            SowType = sow.SowType.ToString(),
            SowStartDate = sow.SowStartDate,
            SowEndDate = sow.SowEndDate,
            RateIncrease = seesRateIncreaseAndNotes ? sow.RateIncrease : null,
            Note = seesRateIncreaseAndNotes ? sow.Note : null,
        })];
    }

    /// <summary>
    /// Projects a <see cref="Client"/> to the published <see cref="CompassClientDto"/>, deriving its
    /// status here from the client's assignment fields directly.
    /// </summary>
    private static CompassClientDto ToDto(Client client, string status) => new()
    {
        Id = client.Id,
        ClientName = client.ClientName,
        MsaSignedDate = client.MsaSignedDate,
        NdaSignedDate = client.NdaSignedDate,
        IsInternal = client.IsInternal,
        InvoiceFrequency = client.InvoiceFrequencyType?.TypeName,
        Status = status,
    };

    /// <summary>
    /// Projects a <see cref="ClientAssignment"/> to the published <see cref="CompassAssignmentDto"/>,
    /// substituting a house-standard default whenever no invoice frequency is set.
    /// </summary>
    private static CompassAssignmentDto ToDto(ClientAssignment assignment) => new()
    {
        Id = assignment.Id,
        EmployeeId = assignment.EmployeeId,
        ClientId = assignment.ClientId,
        ClientName = assignment.Client?.ClientName ?? string.Empty,
        StartDate = assignment.StartDate,
        EndDate = assignment.EndDate,
        Note = assignment.Note,
        EffectiveInvoiceFrequency =
            assignment.InvoiceFrequencyType?.TypeName
            ?? assignment.Client?.InvoiceFrequencyType?.TypeName,
    };
}
