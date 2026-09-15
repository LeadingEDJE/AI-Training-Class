using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <inheritdoc cref="ICoachNotificationRepository" />
public class CoachNotificationRepository(LeapDbContext context) : ICoachNotificationRepository
{
    /// <inheritdoc />
    public async Task<CoachNotificationFacts?> GetAssignmentEndFactsAsync(
        int assignmentId,
        CancellationToken cancellationToken
    ) =>
        await context
            .Set<ClientAssignment>()
            .AsNoTracking()
            .Where(assignment => assignment.Id == assignmentId && assignment.EndDate != null)
            .Select(assignment => new CoachNotificationFacts(
                assignment.EmployeeId,
                assignment.Employee!.FirstName + " " + assignment.Employee!.LastName,
                assignment.Client!.ClientName,
                assignment.EndDate!.Value,
                assignment.Employee!.Coach == null ? null : assignment.Employee!.Coach!.Email
            ))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<CoachNotificationFacts?> GetSowExtensionFactsAsync(
        int sowId,
        CancellationToken cancellationToken
    ) =>
        await context
            .Set<Sow>()
            .AsNoTracking()
            .Where(sow => sow.Id == sowId)
            .Select(sow => new CoachNotificationFacts(
                sow.ClientAssignment!.EmployeeId,
                sow.ClientAssignment!.Employee!.FirstName
                    + " "
                    + sow.ClientAssignment!.Employee!.LastName,
                sow.ClientAssignment!.Client!.ClientName,
                // The parent assignment's end date, used here since the SOW's own end date is not
                // meaningful for an extension notice.
                sow.SowEndDate,
                sow.ClientAssignment!.Employee!.Coach == null
                    ? null
                    : sow.ClientAssignment!.Employee!.Coach!.Email
            ))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> HasNotifiedAsync(
        string notificationType,
        CancellationToken cancellationToken
    ) =>
        await context
            .NotificationLogs.AsNoTracking()
            .AnyAsync(log => log.NotificationType == notificationType, cancellationToken);

    /// <inheritdoc />
    public async Task AddLogAsync(NotificationLog log, CancellationToken cancellationToken) =>
        await context.NotificationLogs.AddAsync(log, cancellationToken);
}
