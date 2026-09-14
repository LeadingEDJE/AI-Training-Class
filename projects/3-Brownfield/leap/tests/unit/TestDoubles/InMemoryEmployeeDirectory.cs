#pragma warning disable CS1591 // Phase 39 D-02: intentionally undocumented — test double.

using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Tests.TestDoubles;

/// <remarks>
/// Serves whatever is seeded into <see cref="Employees"/>, empty by default. An empty directory is a
/// legitimate state — the audit actor lookup then resolves no names and falls back to raw identifiers
/// — so a test that expects a resolved name MUST seed one.
/// </remarks>
public class InMemoryEmployeeDirectory : IEmployeeDirectory
{
    public List<EmployeeDirectoryEntry> Employees { get; } = [];

    /// <remarks>
    /// ⚠️ Re-seeding REPLACES the previous entry — matched on <paramref name="id"/> AND on
    /// <paramref name="edjeId"/>. Several suites seed a default employee in their constructor and then
    /// re-seed to vary one field; appending would leave the constructor's entry first and a
    /// by-identity lookup would return the stale one.
    /// </remarks>
    public InMemoryEmployeeDirectory AddEmployee(
        string id,
        string name,
        string firstName = "",
        string lastName = "",
        string? email = null,
        Guid? edjeId = null)
    {
        Employees.RemoveAll(e =>
            string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)
            || (edjeId.HasValue && e.EdjeId == edjeId));
        Employees.Add(new EmployeeDirectoryEntry(id, name, edjeId, email, firstName, lastName));
        return this;
    }

    public Task<IReadOnlyList<EmployeeDirectoryEntry>> GetAllEmployeesAsync() =>
        Task.FromResult<IReadOnlyList<EmployeeDirectoryEntry>>(Employees.ToList());
}
