namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// An employee-type lookup value as the configuration API returns it.
/// </summary>
/// <remarks>
/// Extra optional fields may be added here freely as new admin screens need them; this type carries
/// no contract test pinning its member set the way <c>CompassEmployeeDto</c> does.
/// </remarks>
/// <param name="Id">The lookup's identifier, used when editing it.</param>
/// <param name="TypeName">The display name, unique across employee types.</param>
/// <param name="IsActive">Whether the value is offered when classifying an EDJEr.</param>
public record EmployeeTypeDto(int Id, string TypeName, bool IsActive);
