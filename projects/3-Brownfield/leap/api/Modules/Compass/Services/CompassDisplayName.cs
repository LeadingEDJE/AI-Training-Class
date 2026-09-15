namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Builds a person's display name from a Compass <see cref="Employee"/>'s first and last name, safe
/// to call from inside a LINQ projection since it is a plain string concatenation under the hood.
/// </summary>
internal static class CompassDisplayName
{
    /// <summary>
    /// Joins <paramref name="employee"/>'s first and last name with a single space.
    /// </summary>
    public static string For(Employee employee)
        => string.Join(
            ' ',
            new[] { employee.FirstName, employee.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
}
