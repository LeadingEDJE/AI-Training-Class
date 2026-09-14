namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// Everything a coach notice needs, read in one query.
/// </summary>
/// <remarks>
/// An internal read shape, not a transport DTO — nothing serialises this. It exists so the notifier
/// composes a message from one projection instead of walking navigations and issuing a query per
/// field. <see cref="CoachEmail"/> is nullable because <c>Employee.CoachEmployeeId</c> is: an EDJEr
/// with no coach is an ordinary case, the internal non-billable staff, not an error. A null here means
/// skip silently; it must never become a thrown exception or a substitute recipient, or ending their
/// assignment becomes impossible.
/// </remarks>
/// <param name="EmployeeId">The EDJEr the notice is about — the notification log's subject.</param>
/// <param name="EdjerName">Their display name, as the subject and both bodies render it.</param>
/// <param name="ClientName">The client named in the body.</param>
/// <param name="EventDate">The assignment end date or the SOW end date, depending on the trigger.</param>
/// <param name="CoachEmail">The coach's address, or <c>null</c> when the EDJEr has no coach.</param>
public sealed record CoachNotificationFacts(
    int EmployeeId,
    string EdjerName,
    string ClientName,
    DateOnly EventDate,
    string? CoachEmail
);
