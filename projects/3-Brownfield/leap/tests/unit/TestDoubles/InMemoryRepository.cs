#pragma warning disable CS1591 // Phase 39 D-02: intentionally undocumented — test double.
using System.Reflection;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Tests.TestDoubles;

public class InMemoryRepository<T> : IRepository<T> where T : class
{
    private readonly List<T> _entities = [];
    private int _nextId = 1;

    private static readonly PropertyInfo IdProperty =
        typeof(T).GetProperty("Id") ?? throw new InvalidOperationException(
            $"Type {typeof(T).Name} does not have an Id property.");

    public Task<IReadOnlyList<T>> GetAllAsync()
    {
        return Task.FromResult<IReadOnlyList<T>>(_entities.ToList());
    }

    public Task<T?> GetByIdAsync(int id)
    {
        T? entity = _entities.FirstOrDefault(e => (int)IdProperty.GetValue(e)! == id);
        return Task.FromResult(entity);
    }

    public Task<T> CreateAsync(T entity)
    {
        IdProperty.SetValue(entity, _nextId++);
        _entities.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<T?> UpdateAsync(int id, T entity)
    {
        T? existing = _entities.FirstOrDefault(e => (int)IdProperty.GetValue(e)! == id);
        if (existing is null)
        {
            return Task.FromResult<T?>(null);
        }

        foreach (PropertyInfo property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name == "Id" || !property.CanWrite)
            {
                continue;
            }

            property.SetValue(existing, property.GetValue(entity));
        }

        return Task.FromResult<T?>(existing);
    }

    public Task<bool> DeleteAsync(int id)
    {
        T? existing = _entities.FirstOrDefault(e => (int)IdProperty.GetValue(e)! == id);
        if (existing is null)
        {
            return Task.FromResult(false);
        }

        _entities.Remove(existing);
        return Task.FromResult(true);
    }
}
