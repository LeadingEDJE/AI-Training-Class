using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// <see cref="IEmployeeDirectory"/> backed by <c>people</c> — the sole surviving directory table after
/// the Timesheet/Ooto module retirement (the frozen legacy <c>employees</c> table these Platform
/// callers once read is dropped).
/// </summary>
public class PersonEmployeeDirectory(LeapDbContext context) : IEmployeeDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<EmployeeDirectoryEntry>> GetAllEmployeesAsync()
    {
        var people = await context.People.AsNoTracking().ToListAsync();

        return people
            .Select(p => new EmployeeDirectoryEntry(
                p.Id.ToString(),
                FormatName(p.FirstName, p.LastName) ?? p.Email ?? p.Id.ToString(),
                p.EdjeId,
                p.Email,
                p.FirstName ?? string.Empty,
                p.LastName ?? string.Empty))
            .ToList();
    }

    private static string? FormatName(string? firstName, string? lastName)
    {
        var name = string.Join(' ', new[] { firstName, lastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
