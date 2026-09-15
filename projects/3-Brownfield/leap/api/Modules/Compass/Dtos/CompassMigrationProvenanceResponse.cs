namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>One migrated record, and the TPS row it came from.</summary>
/// <param name="LegacyTpsId">The qualified source identifier, e.g. <c>tps:Edjer:1001</c>.</param>
/// <param name="CompassId">The identifier Compass assigned.</param>
public sealed record CompassMigrationProvenanceEntry(string LegacyTpsId, int CompassId);

/// <summary>Everything a TPS migration has created so far, across every Compass table.</summary>
/// <param name="Entries">
/// One entry per migrated record, grouped by table internally even though the qualifier inside
/// <see cref="CompassMigrationProvenanceEntry.LegacyTpsId"/> also names the source.
/// </param>
public sealed record CompassMigrationProvenanceResponse(
    IReadOnlyList<CompassMigrationProvenanceEntry> Entries
);
