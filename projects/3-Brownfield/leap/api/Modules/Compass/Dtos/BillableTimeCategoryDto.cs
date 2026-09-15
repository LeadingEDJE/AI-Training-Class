namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// One client-scoped billable time category.
/// </summary>
/// <param name="Id">The category's identity key.</param>
/// <param name="CategoryName">The display name.</param>
/// <param name="IsActive">Whether the category is offered.</param>
public sealed record BillableTimeCategoryDto(int Id, string CategoryName, bool IsActive);
