using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>Reads <c>legacy_tps_id</c> back from the four tables a TPS migration writes to.</summary>
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
