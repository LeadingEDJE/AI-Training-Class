using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>
/// Reads <c>legacy_tps_id</c> back from the five tables a TPS migration writes to.
/// </summary>
/// <remarks>
/// Five separate queries rather than one union, because EF composes a union over heterogeneous
/// entity types poorly: each table holds at most a few thousand rows and this is read once per
/// migration run. Every query filters before it projects, and nothing follows the projection but
/// the terminator, because ordering or
/// filtering by a member of a type constructed inside the projection answers HTTP 500 at runtime
/// while passing every InMemory-provider unit test.
/// </remarks>
/// <param name="context">The single application context.</param>
public class CompassMigrationProvenanceRepository(LeapDbContext context)
    : ICompassMigrationProvenanceRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CompassMigrationProvenanceEntry>> GetAllAsync(
        CancellationToken cancellationToken
    )
    {
        var entries = new List<CompassMigrationProvenanceEntry>();

        entries.AddRange(
            await context
                .Set<Employee>()
                .Where(row => row.LegacyTpsId != null)
                .Select(row => new CompassMigrationProvenanceEntry(row.LegacyTpsId!, row.Id))
                .ToListAsync(cancellationToken)
        );

        entries.AddRange(
            await context
                .Set<Client>()
                .Where(row => row.LegacyTpsId != null)
                .Select(row => new CompassMigrationProvenanceEntry(row.LegacyTpsId!, row.Id))
                .ToListAsync(cancellationToken)
        );

        entries.AddRange(
            await context
                .Set<ClientAssignment>()
                .Where(row => row.LegacyTpsId != null)
                .Select(row => new CompassMigrationProvenanceEntry(row.LegacyTpsId!, row.Id))
                .ToListAsync(cancellationToken)
        );

        entries.AddRange(
            await context
                .Set<Sow>()
                .Where(row => row.LegacyTpsId != null)
                .Select(row => new CompassMigrationProvenanceEntry(row.LegacyTpsId!, row.Id))
                .ToListAsync(cancellationToken)
        );

        entries.AddRange(
            await context
                .Set<BillableTimeCategory>()
                .Where(row => row.LegacyTpsId != null)
                .Select(row => new CompassMigrationProvenanceEntry(row.LegacyTpsId!, row.Id))
                .ToListAsync(cancellationToken)
        );

        return entries;
    }
}
