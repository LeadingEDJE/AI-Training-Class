using System.Linq.Expressions;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>The single shared derivation of assignment currency (BR-7) and client status (BR-11).</summary>
/// <remarks>
/// Read the contract, not AC-42's PRD wording: they disagree on the exactly-today case, and the owner decision
/// the contract records — active for the whole calendar day — wins. That is the case AC-42 itself
/// names as the one independent implementations get wrong. BR-7 and BR-11 are the same predicate,
/// existentially quantified: a client is Active exactly when it holds at least one current assignment.
/// <see cref="HasEverBeenAssigned"/> is the one predicate not derived from it, and reads no dates.
/// List consumers must take the expression shapes, not <see cref="Of"/> — per-row derivation is an
/// N+1 against AC-NFR-4. Status gates nothing: it never filters eligibility, editing or authorization.
/// </remarks>
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
    /// <remarks>
    /// Composed from <see cref="IsCurrent"/> rather than restating the comparison, so the two agree by
    /// construction on the exactly-today boundary — an assignment ending today must be both current
    /// and future-dated, or the surfaces composed from these two predicates (the confirmed-rollouts
    /// population and the Availability report's unserved-days total, which both turn on this boundary)
    /// would disagree about the same EDJEr on the same day.
    /// </remarks>
    /// <param name="today">The business date.</param>
    /// <returns>An expression, composable into a query.</returns>
    Expression<Func<ClientAssignment, bool>> IsFutureDated(DateOnly today);

    /// <summary>
    /// Whether an assignment has begun (FR-032): its start date is today or earlier.
    /// </summary>
    /// <remarks>
    /// Composed with <see cref="IsCurrent"/> to express "active". <see cref="IsCurrent"/> is
    /// <c>EndDate == null || EndDate &gt;= today</c> and never reads the start date, so on its own it
    /// means not ended, not active — a future-dated assignment satisfies it. An active assignment is
    /// <c>IsCurrent(today) &amp;&amp; HasStarted(today)</c>, which FR-032 states once for every
    /// surface. Kept separate rather than folded into <see cref="IsCurrent"/> so "not ended" stays
    /// available to the callers that want it. Inclusive of today, matching <see cref="IsCurrent"/>'s
    /// boundary. It is the only member that reads <see cref="ClientAssignment.StartDate"/>; the
    /// one-derivation rule (BR-11) is what keeps it here.
    /// </remarks>
    /// <param name="today">The business date.</param>
    /// <returns>An expression, composable into a query.</returns>
    Expression<Func<ClientAssignment, bool>> HasStarted(DateOnly today);

    /// <summary>
    /// Whether a SOW's end date falls within the given number of days of today, inclusive at both
    /// ends (FR-003, BR-5).
    /// </summary>
    /// <remarks>
    /// The gate's regex matches the bare substring "EndDate", so it also fences
    /// <see cref="Compass.Sow.SowEndDate"/> comparisons even though it was written about assignment
    /// currency — see <c>ClientStatusSingleDerivationTests</c>' remarks. Housed here rather than in a
    /// class named for SOWs for that reason.
    /// </remarks>
    /// <param name="today">The business date.</param>
    /// <param name="days">The window size in days; the boundary day itself qualifies.</param>
    /// <returns>An expression, composable into a query.</returns>
    Expression<Func<Sow, bool>> IsExpiringWithin(DateOnly today, int days);

    /// <summary>
    /// Whether a SOW is active: it has begun and has not ended — its start date is today or earlier
    /// and its end date is today or later, inclusive at both ends.
    /// </summary>
    /// <remarks>
    /// The Sales Dashboard's first tile counts these. Both dates are required columns
    /// (<c>SowConfiguration.cs</c>) and non-nullable <see cref="DateOnly"/> on the CLR type, so there
    /// is no null case to guard — unlike <see cref="IsCurrent"/>, which reads a nullable
    /// <see cref="ClientAssignment.EndDate"/>. Housed here rather than in a class named for SOWs
    /// because <c>ClientStatusSingleDerivationTests</c> fences every <c>SowEndDate</c> comparison
    /// against a business date, exactly as it does for <see cref="IsExpiringWithin"/>.
    /// </remarks>
    /// <param name="today">The business date.</param>
    /// <returns>An expression, composable into a query.</returns>
    Expression<Func<Sow, bool>> IsActiveSow(DateOnly today);

    /// <summary>
    /// Whether no other SOW on the same assignment starts after this one's end date (FR-003, and
    /// BR-5's no-follow-on condition).
    /// </summary>
    /// <remarks>
    /// A SOW that merely exists on the assignment is not a follow-on if it precedes this one — it may
    /// be the prior contract period. Existence-only would silently under-report the dashboard's
    /// expiring-SOW tile (research D-3).
    /// </remarks>
    /// <returns>An expression, composable into a query.</returns>
    Expression<Func<Sow, bool>> HasNoFollowOn();

    /// <summary>
    /// Whether a SOW sits on an assignment that is open-ended and active: the assignment has no end
    /// date and has already started.
    /// </summary>
    /// <remarks>
    /// The scope of the expiring-SOWs population. An assignment carrying an end date is a planned
    /// rollout — its departure is already handled, so its expiring SOW needs no alert and is excluded.
    /// A null end date is the "unconfirmed rollout" the alert exists for. Not composed from
    /// <see cref="IsCurrent"/>, which also admits a non-null future end date: the condition is
    /// <c>EndDate == null</c> specifically, an equality against null rather than a currency comparison
    /// (which is why <c>ClientStatusSingleDerivationTests</c> does not fence it). The
    /// <c>StartDate &lt;= today</c> half is the same inclusive boundary as <see cref="HasStarted"/>.
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
    /// <remarks>
    /// For detail views. Using this inside a loop over a collection reintroduces the N+1 the
    /// expression shapes exist to avoid.
    /// </remarks>
    ClientStatus Of(Client client, DateOnly today);

    /// <summary>
    /// Whether a client has ever held an assignment — regardless of whether any of them is current.
    /// </summary>
    /// <remarks>
    /// The whole of what separates <see cref="ClientStatus.Inactive"/> from
    /// <see cref="ClientStatus.Former"/>. It reads no dates: written as "has an assignment that ended"
    /// it would be a second currency comparison, the divergence BR-11 exists to prevent, so it counts
    /// rows instead and <c>HasEverBeenAssigned_LooksAtNoDates_SoItCannotDisagreeWithIsCurrent</c> pins
    /// that. A predicate over clients, never a set of them: it does not filter, decide eligibility or
    /// gate editing, the shape <c>ClientStatusNonGatingTests</c> protects. An Inactive client and a
    /// Former one are both fully selectable and fully editable.
    /// </remarks>
    /// <returns>An expression, so a list of N clients derives status in one round trip.</returns>
    Expression<Func<Client, bool>> HasEverBeenAssigned();

    /// <summary>
    /// Names a client's status from set-wise evaluations of <see cref="IsActive"/> and
    /// <see cref="HasEverBeenAssigned"/>.
    /// </summary>
    /// <remarks>
    /// Lets a list consumer derive set-wise and still obtain the value from the derivation rather than
    /// naming <c>ClientStatus.Active</c> itself, which <c>ClientStatusSingleDerivationTests</c> forbids:
    /// a second place naming those values can name them wrongly. Named for the level it applies to, as
    /// its <see cref="StatusOfAssignment"/> sibling is; the two replaced one <c>From(bool)</c> that
    /// meant different things at different call sites, and a caller must not be able to pick the wrong
    /// one and lose <see cref="ClientStatus.Former"/> silently (see <see cref="AssignmentStatus"/>).
    /// </remarks>
    /// <param name="holdsCurrentAssignment">Whether the client satisfied <see cref="IsActive"/>.</param>
    /// <param name="hasEverBeenAssigned">Whether the client satisfied <see cref="HasEverBeenAssigned"/>.</param>
    ClientStatus StatusOfClient(bool holdsCurrentAssignment, bool hasEverBeenAssigned);

    /// <summary>
    /// Names one assignment's currency from a set-wise evaluation of <see cref="IsCurrent"/>
    /// (composed with <see cref="HasStarted"/> where the caller means "active").
    /// </summary>
    /// <remarks>
    /// Binary, and typed so it cannot be otherwise: an assignment has no
    /// <see cref="ClientStatus.Former"/> state. Publishes the same two words the assignment-history
    /// rows have always carried.
    /// </remarks>
    /// <param name="isCurrent">Whether the assignment satisfied the caller's currency predicate.</param>
    AssignmentStatus StatusOfAssignment(bool isCurrent);
}
