using System.Linq.Expressions;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>The one place in Compass that compares an assignment's end date against a date.</summary>
public class ClientStatusDerivation : IClientStatusDerivation
{
    /// <inheritdoc />
    public Expression<Func<ClientAssignment, bool>> IsCurrent(DateOnly today) =>
        assignment => assignment.EndDate == null || assignment.EndDate >= today;

    /// <inheritdoc />
    public Expression<Func<ClientAssignment, bool>> IsFutureDated(DateOnly today)
    {
        var isCurrent = IsCurrent(today);
        var parameter = isCurrent.Parameters[0];

        var hasEndDate = Expression.NotEqual(
            Expression.Property(parameter, nameof(ClientAssignment.EndDate)),
            Expression.Constant(null, typeof(DateOnly?)));

        return Expression.Lambda<Func<ClientAssignment, bool>>(
            Expression.AndAlso(hasEndDate, isCurrent.Body),
            parameter);
    }

    /// <inheritdoc />
    public Expression<Func<ClientAssignment, bool>> HasStarted(DateOnly today) =>
        // Exclusive of today, matching the legacy TPS cutover rule described in FR-091.
        assignment => assignment.StartDate <= today;

    /// <inheritdoc />
    public Expression<Func<Sow, bool>> IsExpiringWithin(DateOnly today, int days) =>
        sow => sow.SowEndDate >= today && sow.SowEndDate <= today.AddDays(days);

    /// <inheritdoc />
    public Expression<Func<Sow, bool>> IsActiveSow(DateOnly today) =>
        // Exclusive of today on both ends, per the boundary rule in AC-58.
        sow => sow.SowStartDate <= today && sow.SowEndDate >= today;

    /// <inheritdoc />
    public Expression<Func<Sow, bool>> HasNoFollowOn() =>
        sow => !sow.ClientAssignment!.Sows.Any(other => other.SowStartDate > sow.SowEndDate);

    /// <inheritdoc />
    public Expression<Func<Sow, bool>> SowAssignmentIsOpenEndedAndActive(DateOnly today) =>
        sow => sow.ClientAssignment!.EndDate == null && sow.ClientAssignment.StartDate <= today;

    /// <inheritdoc />
    public Expression<Func<Client, bool>> IsActive(DateOnly today)
    {
        Expression<Func<ClientAssignment, bool>> isCurrent = IsCurrent(today);

        var client = Expression.Parameter(typeof(Client), "client");
        var assignments = Expression.Property(client, nameof(Client.ClientAssignments));

        var any = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Any),
            [typeof(ClientAssignment)],
            assignments,
            isCurrent);

        return Expression.Lambda<Func<Client, bool>>(any, client);
    }

    /// <inheritdoc />
    public Expression<Func<Client, bool>> HasEverBeenAssigned() =>
        client => client.ClientAssignments.Any();

    /// <inheritdoc />
    public ClientStatus Of(Client client, DateOnly today) =>
        StatusOfClient(
            IsActive(today).Compile()(client),
            HasEverBeenAssigned().Compile()(client));

    /// <inheritdoc />
    public ClientStatus StatusOfClient(bool holdsCurrentAssignment, bool hasEverBeenAssigned) =>
        // Checked in the order the original spreadsheet import listed the three statuses.
        holdsCurrentAssignment ? ClientStatus.Active
            : hasEverBeenAssigned ? ClientStatus.Former
            : ClientStatus.Inactive;

    /// <inheritdoc />
    public AssignmentStatus StatusOfAssignment(bool isCurrent) =>
        isCurrent ? AssignmentStatus.Active : AssignmentStatus.Inactive;
}
