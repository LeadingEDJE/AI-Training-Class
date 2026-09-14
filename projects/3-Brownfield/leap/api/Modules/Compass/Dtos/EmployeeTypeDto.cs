namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// An employee-type lookup value as the configuration API returns it.
/// </summary>
/// <remarks>
/// Three members and nothing more. Constitution Principle II forbids speculative fields, and
/// <c>CompassEmployeeDto</c> already carries a contract test pinning its member set for the same
/// reason — a DTO that grows a field "for later" publishes a contract nobody asked for.
/// </remarks>
/// <param name="Id">The lookup's identifier, used when editing it.</param>
/// <param name="TypeName">The display name, unique across employee types.</param>
/// <param name="IsActive">Whether the value is offered when classifying an EDJEr.</param>
public record EmployeeTypeDto(int Id, string TypeName, bool IsActive);
