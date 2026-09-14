using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Platform.Data.Repositories;

/// <summary>EF Core repository for absorbed <see cref="Person"/> records (Guid uuid PK). No SaveChangesAsync — the service owns persistence.</summary>
public class PersonRepository(LeapDbContext context) : IPersonRepository
{
    private readonly DbSet<Person> _dbSet = context.People;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Person>> GetAllAsync()
        => await _dbSet.AsNoTracking().OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ToListAsync();

    /// <inheritdoc />
    public async Task<Person?> GetByIdAsync(Guid id)
        => await _dbSet.FirstOrDefaultAsync(p => p.Id == id);

    /// <inheritdoc />
    public async Task<Person?> GetByEdjeIdAsync(Guid edjeId)
        => await _dbSet.AsNoTracking().FirstOrDefaultAsync(p => p.EdjeId == edjeId);

    /// <inheritdoc />
    public async Task<bool> EmailExistsAsync(string email, Guid? excludeId)
    {
        // ToLower() translates to Postgres lower(), matching the ix_people_email_lower functional index.
        var lower = email.ToLower();
        var query = _dbSet.Where(p => p.Email != null && p.Email.ToLower() == lower);
        if (excludeId.HasValue)
        {
            query = query.Where(p => p.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }

    /// <inheritdoc />
    public async Task<Person> AddAsync(Person person)
    {
        await _dbSet.AddAsync(person);
        return person;
    }
}
