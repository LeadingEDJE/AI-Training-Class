#pragma warning disable CS1591 // Phase 39 D-02: intentionally undocumented — test double.

using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
namespace LeadingEDJE.Leap.Api.Tests.TestDoubles;

public class InMemoryPersonRepository : IPersonRepository
{
    private readonly List<Person> _people = [];

    public Task<IReadOnlyList<Person>> GetAllAsync()
        => Task.FromResult<IReadOnlyList<Person>>(_people.ToList());

    public Task<Person?> GetByIdAsync(Guid id)
        => Task.FromResult(_people.FirstOrDefault(p => p.Id == id));

    public Task<Person?> GetByEdjeIdAsync(Guid edjeId)
        => Task.FromResult(_people.FirstOrDefault(p => p.EdjeId == edjeId));

    public Task<bool> EmailExistsAsync(string email, Guid? excludeId)
        => Task.FromResult(_people.Any(p =>
            p.Email != null
            && string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase)
            && (excludeId is null || p.Id != excludeId.Value)));

    public Task<Person> AddAsync(Person person)
    {
        _people.Add(person);
        return Task.FromResult(person);
    }
}
