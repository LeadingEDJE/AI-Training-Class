using System.Linq.Expressions;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>The one place in Compass that compares an assignment's end date against a date.</summary>
/// <remarks>
/// Fenced by a build gate: <c>tests/unit/Architecture/ClientStatusSingleDerivationTests.cs</c> fails
/// any other production file that compares <c>EndDate</c> or constructs a
/// <see cref="ClientStatus"/>. AC-42 states the reason — independent implementations would diverge on
/// boundary cases such as an end date falling exactly today. <see cref="IsCurrent"/> is the primitive
/// that <see cref="IsFutureDated"/> and <see cref="IsActive"/> compose from rather than restating the
/// comparison, which is inclusive of today, and every expression here is EF-translatable.
/// <see cref="HasEverBeenAssigned"/> stands apart: it answers whether the client was ever engaged and
/// reads no dates at all, so it cannot disagree with the others on any boundary.
/// </remarks>
public class ClientStatusDerivation : IClientStatusDerivation
{
    /// <inheritdoc />
    public Expression<Func<ClientAssignment, bool>> IsCurrent(DateOnly today) =>
        assignment => assignment.EndDate == null || assignment.EndDate >= today;

    /// <inheritdoc />
    public Expression<Func<ClientAssignment, bool>> IsFutureDated(DateOnly today)
    {
        // Composed from IsCurrent's own expression tree — sharing its ParameterExpression instance, so
        // no parameter-rebinding visitor is needed — rather than restating ">= today", so the two
        // cannot drift on the exactly-today boundary (contract date-predicates.md). `EndDate != null`
        // ANDed onto IsCurrent's body reduces to "EndDate != null && EndDate >= today", since
        // IsCurrent's left disjunct is false whenever the new conjunct holds.
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
        // `<=`, not `<`: inclusive of today, matching IsCurrent's boundary. Not composed from
        // IsCurrent, because it is not a refinement of it — it is the orthogonal half of "active"
        // (FR-032), and the two are ANDed at the call site rather than nested here.
        assignment => assignment.StartDate <= today;

    /// <inheritdoc />
    public Expression<Func<Sow, bool>> IsExpiringWithin(DateOnly today, int days) =>
        // Sow.SowEndDate is a REQUIRED column (SowConfiguration.cs) and a non-nullable DateOnly in the
        // CLR type, so there is no null case to guard — unlike ClientAssignment.EndDate above.
        sow => sow.SowEndDate >= today && sow.SowEndDate <= today.AddDays(days);

    /// <inheritdoc />
    public Expression<Func<Sow, bool>> IsActiveSow(DateOnly today) =>
        // Both SowStartDate and SowEndDate are required, non-nullable DateOnly columns, so no null
        // guard is needed. Inclusive of today on both ends, matching HasStarted's `<=`
        // and IsCurrent's `>=` boundaries: a SOW starting this morning or ending tonight is active
        // for the whole calendar day. `>` on either side is the silent defect AC-42 warns of.
        sow => sow.SowStartDate <= today && sow.SowEndDate >= today;

    /// <inheritdoc />
    public Expression<Func<Sow, bool>> HasNoFollowOn() =>
        // A correlated EXISTS over the navigation, not a per-row lookup (FR-021) — EF Core translates
        // this into one subquery per outer row evaluated in the SAME database round trip, not one
        // query per SOW. `sow.ClientAssignment` is a required FK's navigation; `!` only suppresses the
        // compiler's nullable warning and has no effect on the emitted expression tree.
        sow => !sow.ClientAssignment!.Sows.Any(other => other.SowStartDate > sow.SowEndDate);

    /// <inheritdoc />
    public Expression<Func<Sow, bool>> SowAssignmentIsOpenEndedAndActive(DateOnly today) =>
        // `EndDate == null` is an equality against null, a different question from currency, so
        // ClientStatusSingleDerivationTests deliberately does not fence it. A non-null end date is a
        // planned rollout and its SOW is excluded; IsCurrent is the wrong tool because it also admits a
        // future non-null end date. `StartDate <= today` restates HasStarted's inclusive boundary
        // rather than rebinding it onto this Sow parameter; StartDate carries no exactly-today risk.
        sow => sow.ClientAssignment!.EndDate == null && sow.ClientAssignment.StartDate <= today;

    /// <inheritdoc />
    public Expression<Func<Client, bool>> IsActive(DateOnly today)
    {
        // Composed from IsCurrent rather than restating the comparison, so the two cannot drift.
        // Inlined into Any(...) below, which keeps the whole thing translatable to SQL.
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
        // No date comparison, deliberately: "has this client ever been engaged" is a different
        // question from "is it engaged now", and writing it as "has an assignment that ended" would
        // be a second currency derivation — the divergence BR-11 forbids. EF Core renders this as a
        // correlated EXISTS over compass.client_assignment, covered by ix_client_assignment_client_id.
        client => client.ClientAssignments.Any();

    /// <inheritdoc />
    public ClientStatus Of(Client client, DateOnly today) =>
        // Compiles the two expressions rather than restating either rule, so the per-client shape and
        // the set-wise one cannot drift — asserted by
        // StatusOfClient_AgreesWithOf_ForEveryArrangementOfAssignments.
        StatusOfClient(
            IsActive(today).Compile()(client),
            HasEverBeenAssigned().Compile()(client));

    /// <inheritdoc />
    public ClientStatus StatusOfClient(bool holdsCurrentAssignment, bool hasEverBeenAssigned) =>
        // Holding a current assignment is checked FIRST, so the fourth (unreachable) row of the truth
        // table — current but never assigned — still answers Active rather than falling through to a
        // never-engaged word. Totality is the property the Client Directory's sort depends on, so no
        // branch here may throw or return null.
        holdsCurrentAssignment ? ClientStatus.Active
            : hasEverBeenAssigned ? ClientStatus.Former
            : ClientStatus.Inactive;

    /// <inheritdoc />
    public AssignmentStatus StatusOfAssignment(bool isCurrent) =>
        isCurrent ? AssignmentStatus.Active : AssignmentStatus.Inactive;
}
