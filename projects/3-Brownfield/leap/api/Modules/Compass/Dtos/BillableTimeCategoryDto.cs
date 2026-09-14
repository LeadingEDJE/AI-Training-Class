namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// One client-scoped billable time category.
/// </summary>
/// <remarks>
/// The active flag here is the CATEGORY's own (FR-024), and is unrelated to the client's derived status
/// — a category is deactivated by hand through <see cref="UpdateBillableTimeCategoryRequest"/>, whereas
/// a client's status is computed from its assignments and cannot be set at all. The two words look alike
/// and mean different things; this is the one place a reader is likely to conflate them.
/// </remarks>
/// <param name="Id">The category's identity key.</param>
/// <param name="CategoryName">The display name. Unique PER CLIENT, not globally (FR-025).</param>
/// <param name="IsActive">Whether the category is offered. Retired categories are kept, never deleted.</param>
public sealed record BillableTimeCategoryDto(int Id, string CategoryName, bool IsActive);
