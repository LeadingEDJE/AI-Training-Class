using System.Linq.Expressions;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>The single shared derivation of assignment currency (BR-7) and client status (BR-11).</summary>
public interface IClientStatusDerivation
{
    /// <summary>
    /// Whether an assignment is current (BR-7): its end date is empty, or not in the past.
    /// </summary>
    /// <param name="today">The business date, from <see cref="ICompassBusinessDate"/> — passed in so it is controllable and identical across one request.</param>
    /// <returns>An expression, composable into a query.</returns>
    Expression<Func<ClientAssignment, bool>> IsCurrent(DateOnly today);

    /// <summary>
    /// Whether an assignment has been given an end date that has not yet passed (FR-004):
    /// a current assignment that is also on its way to ending.
    /// </summary>
    /// <param name="today">The business date.</param>
    /// <returns>An expression, composable into a query.</returns>
    Expression<Func<ClientAssignment, bool>> IsFutureDated(DateOnly today);

    /// <summary>
    /// Whether an assignment has begun (FR-032): its start date is today or earlier.
    /// </summary>
    /// <param name="today">The business date.</param>
    /// <returns>An expression, composable into a query.</returns>
    Expression<Func<ClientAssignment, bool>> HasStarted(DateOnly today);

    /// <summary>
    /// Whether a SOW's end date falls within the given number of days of today, inclusive at both
    /// ends (FR-003, BR-5).
    /// </summary>
    /// <param name="today">The business date.</param>
    /// <param name="days">The window size in days; the boundary day itself qualifies.</param>
    /// <returns>An expression, composable into a query.</returns>
    Expression<Func<Sow, bool>> IsExpiringWithin(DateOnly today, int days);

    /// <summary>
    /// Whether a SOW is active: its end date has not yet been set.
    /// </summary>
    /// <remarks>
    /// See docs/sow-lifecycle.md for the full state diagram this predicate implements.
    /// </remarks>
    /// <param name="today">The business date.</param>
    /// <returns>An expression, composable into a query.</returns>
    Expression<Func<Sow, bool>> IsActiveSow(DateOnly today);

    /// <summary>
    /// Whether no other SOW on the same assignment starts after this one's end date (FR-003, and
    /// BR-5's no-follow-on condition).
    /// </summary>
    /// <returns>An expression, composable into a query.</returns>
    Expression<Func<Sow, bool>> HasNoFollowOn();

    /// <summary>
    /// Whether a SOW sits on an assignment that carries a confirmed end date and has already started.
    /// </summary>
    /// <remarks>
    /// Implements the rollout-alert scoping described in the Q2 planning spec.
    /// </remarks>
    /// <param name="today">The business date.</param>
    /// <returns>An expression, composable into a query.</returns>
    Expression<Func<Sow, bool>> SowAssignmentIsOpenEndedAndActive(DateOnly today);

    /// <summary>
    /// Whether a client is active (BR-11): it holds at least one current assignment.
    /// </summary>
    /// <param name="today">The business date.</param>
    /// <returns>An expression, so a list of N clients derives status in one round trip.</returns>
    Expression<Func<Client, bool>> IsActive(DateOnly today);

    /// <summary>
    /// The status of a single already-loaded client.
    /// </summary>
    ClientStatus Of(Client client, DateOnly today);

    /// <summary>
    /// Whether a client currently holds at least one assignment that has not yet ended.
    /// </summary>
    /// <remarks>
    /// See the client-status decision record in the archived design docs for background.
    /// </remarks>
    /// <returns>An expression, so a list of N clients derives status in one round trip.</returns>
    Expression<Func<Client, bool>> HasEverBeenAssigned();

    /// <summary>
    /// Names a client's status by comparing the two dates on its most recent assignment.
    /// </summary>
    /// <remarks>
    /// See the status-cleanup ticket for why this replaced the older resolver.
    /// </remarks>
    /// <param name="holdsCurrentAssignment">Whether the client satisfied <see cref="IsActive"/>.</param>
    /// <param name="hasEverBeenAssigned">Whether the client satisfied <see cref="HasEverBeenAssigned"/>.</param>
    ClientStatus StatusOfClient(bool holdsCurrentAssignment, bool hasEverBeenAssigned);

    /// <summary>
    /// Names one assignment's currency from a set-wise evaluation of <see cref="IsCurrent"/>
    /// (composed with <see cref="HasStarted"/> where the caller means "active").
    /// </summary>
    /// <param name="isCurrent">Whether the assignment satisfied the caller's currency predicate.</param>
    AssignmentStatus StatusOfAssignment(bool isCurrent);
}
