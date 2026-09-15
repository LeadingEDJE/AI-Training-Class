namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// An invoice-frequency lookup value as the configuration API returns it.
/// </summary>
/// <param name="Id">The lookup's identifier, used when editing it.</param>
/// <param name="TypeName">The display name, unique across invoice frequency types.</param>
/// <param name="IsActive">Whether the value is offered as a client's invoicing default.</param>
public record InvoiceFrequencyTypeDto(int Id, string TypeName, bool IsActive);
