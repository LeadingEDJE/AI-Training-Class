using LeadingEDJE.Leap.Api.Modules.Compass.Authorization;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Implements the Compass directory boundary: projects a directory record to the module's DTOs.
/// </summary>
/// <remarks>
/// Business logic lives here; data access lives in the repository. This service performs no writes —
/// it never calls <c>SaveChangesAsync</c>, and it never touches the database context.
/// </remarks>
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

        // Same shape as GetEmployeeAsync: return before the tier is resolved, so a lookup that
        // misses never reads ICurrentUserContext. That is not a micro-optimisation — the real
        // CurrentUserContext throws outside an HTTP request, so a caller exercising only the
        // not-found path would otherwise look safe and be broken for a real one.
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

        // Resolved ONCE for the whole batch, not per row. The tier is a property of the caller, so
        // resolving it per employee would be both wasteful and misleading to read.
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

        // One tier resolution for the whole list, like GetEmployeesByEmailAsync.
        var tier = CompassViewerTier.Resolve(currentUser.Privileges);

        return [.. employees.Select(employee => ToDto(employee, tier))];
    }

    /// <summary>
    /// Projects one Compass employee to the published contract at the given viewer tier.
    /// </summary>
    /// <remarks>
    /// Shared by all three entry points so they cannot drift:
    /// <see cref="CompassEmployeeDto.TimeTracking"/> is the boundary's only tier-gated member, and a
    /// second hand-written projection is a second place for that gate to be forgotten — where
    /// forgetting it discloses the three flags to every caller and nothing fails.
    /// <see cref="CompassEmployeeDto.Timezone"/> and <see cref="CompassEmployeeDto.IsDeliveryTeam"/>
    /// are published unconditionally and deliberately: they are ordinary directory attributes, and the
    /// consumers they exist for reach this read in-process holding no Compass privilege, so they
    /// resolve to Baseline — a gate here would withhold them from the only callers asking.
    /// </remarks>
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
    /// <remarks>
    /// <see cref="CompassDirectorySowDto.RateIncrease"/> and <see cref="CompassDirectorySowDto.Note"/>
    /// are gated on <see cref="CompassTierVisibility.SeesOthersSowsAndNotes"/>, resolved through the
    /// SAME <see cref="CompassViewerTier.Resolve"/> call <see cref="GetEmployeeAsync"/> uses — reusing
    /// the one tier-resolution path rather than introducing a second.
    /// </remarks>
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
    /// Projects a <see cref="Client"/> and its ALREADY-DERIVED status to the published
    /// <see cref="CompassClientDto"/>.
    /// </summary>
    /// <remarks>
    /// The status arrives as a string from the repository, which obtained it from
    /// <c>IClientStatusDerivation.StatusOfClient</c>. This method must not re-derive it, name a status
    /// value itself, or hold the enum — <c>ClientStatusNonGatingTests</c> fails the build on the last
    /// of those, and the other two are the second implementation BR-11 forbids.
    /// </remarks>
    /// <param name="client">The materialized Compass client.</param>
    /// <param name="status">The derived status, already a string.</param>
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
    /// Projects a <see cref="ClientAssignment"/> to the published <see cref="CompassAssignmentDto"/>.
    /// </summary>
    /// <remarks>
    /// Constructed only after the repository has materialized the entity, never inside a LINQ
    /// expression tree (the projected-member trap).
    /// <see cref="CompassAssignmentDto.EffectiveInvoiceFrequency"/> reuses the same expression
    /// <c>CompassAssignmentService.ToDto</c> uses for the audited write surface's read-back (FR-012):
    /// the override where set, else the client default, else <c>null</c> meaning "none set". Null is
    /// neither an error nor a substitution; no house default is invented here.
    /// </remarks>
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
