namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>One migrated record, and the TPS row it came from.</summary>
/// <param name="LegacyTpsId">
/// The qualified source identifier, e.g. <c>tps:Edjer:1001</c>. Qualified because a TPS identifier is
/// unique only within its own table, so employee 1001 and client 1001 are different things.
/// </param>
/// <param name="CompassId">The identifier Compass assigned.</param>
public sealed record CompassMigrationProvenanceEntry(string LegacyTpsId, int CompassId);

/// <summary>Everything a TPS migration has created so far, across every Compass table.</summary>
/// <param name="Entries">
/// One entry per migrated record. Flat and unkeyed by table on purpose: the qualifier inside
/// <see cref="CompassMigrationProvenanceEntry.LegacyTpsId"/> already names the source, and the tool
/// consuming this looks records up by that string alone.
/// </param>
public sealed record CompassMigrationProvenanceResponse(
    IReadOnlyList<CompassMigrationProvenanceEntry> Entries
);
