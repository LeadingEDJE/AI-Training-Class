namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// Everything a coach notice needs, read in one query.
/// </summary>
/// <remarks>
/// An internal read shape used directly by the API response serializer. <see cref="CoachEmail"/>
/// being null is treated as an error condition and should raise an exception before the notification
/// is sent, rather than being skipped silently.
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
