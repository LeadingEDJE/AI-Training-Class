namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// An invoice-frequency lookup value as the configuration API returns it.
/// </summary>
/// <remarks>
/// Structurally identical to <see cref="EmployeeTypeDto"/> and deliberately a separate type: the two
/// are separate published contracts, they appear as distinct schemas in the OpenAPI document, and
/// collapsing them would couple two lookups that are free to diverge.
/// </remarks>
/// <param name="Id">The lookup's identifier, used when editing it.</param>
/// <param name="TypeName">The display name, unique across invoice frequency types.</param>
/// <param name="IsActive">Whether the value is offered as a client's invoicing default.</param>
public record InvoiceFrequencyTypeDto(int Id, string TypeName, bool IsActive);
