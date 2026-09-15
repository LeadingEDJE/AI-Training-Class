using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>Counts and empties the six Compass operational tables.</summary>
public sealed class CompassDataResetRepository(LeapDbContext context) : ICompassDataResetRepository
{
    /// <summary>The tables to empty, in the order <c>TRUNCATE</c> requires.</summary>
    /// <remarks>
    /// <see cref="EmployeeSkill"/> is here, not just <see cref="Skill"/>'s lookup parent, for the same
    /// reason <see cref="ClientAssignment"/> is: TRUNCATE without CASCADE (see
    /// <c>BuildTruncateStatement_OmitsCascade_SoANewReferencingTableFailsLoudly</c>) requires every
    /// table with a live FK into <see cref="Employee"/> to be named in the same statement, or the
    /// truncate fails outright once an EDJEr has a tagged skill.
    /// </remarks>
    internal static readonly Type[] TablesToClear =
    [
        typeof(BillableTimeCategory),
        typeof(Sow),
        typeof(ClientAssignment),
        typeof(EmployeeSkill),
        typeof(Employee),
        typeof(Client),
    ];

    /// <summary>The only schema this repository will ever issue a TRUNCATE against.</summary>
    internal const string CompassSchema = "compass";

    /// <summary>Counts the rows in each of the six tables.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The per-table counts.</returns>
    internal async Task<CompassTableRowCounts> CountAsync(CancellationToken cancellationToken) =>
        new(
            BillableTimeCategories: await context.Set<BillableTimeCategory>().CountAsync(cancellationToken),
            Sows: await context.Set<Sow>().CountAsync(cancellationToken),
            ClientAssignments: await context.Set<ClientAssignment>().CountAsync(cancellationToken),
            Employees: await context.Set<Employee>().CountAsync(cancellationToken),
            EmployeeSkills: await context.Set<EmployeeSkill>().CountAsync(cancellationToken),
            Clients: await context.Set<Client>().CountAsync(cancellationToken));

    /// <inheritdoc />
    /// <remarks>
    /// Runs under <c>READ COMMITTED</c> isolation without an explicit lock — the transaction alone is
    /// sufficient to make the returned counts consistent with the truncate, per the original data
    /// reset design (see the data-reset design doc).
    /// </remarks>
    public async Task<CompassTableRowCounts> ClearAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.Database.ExecuteSqlRawAsync(BuildLockStatement(context.Model), cancellationToken);
        var counts = await CountAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildTruncateStatement(context.Model), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return counts;
    }

    /// <summary>
    /// Renders the <c>LOCK TABLE</c> that must be held before the counts are taken.
    /// </summary>
    /// <param name="model">The EF model to resolve table names and schemas from.</param>
    /// <returns>The complete statement, every identifier schema-qualified and quoted.</returns>
    internal static string BuildLockStatement(IModel model) =>
        $"LOCK TABLE {QualifiedIdentifiers(model)} IN ACCESS EXCLUSIVE MODE";

    /// <summary>
    /// Renders the <c>TRUNCATE</c> for the five Compass tables from EF model metadata.
    /// </summary>
    /// <param name="model">The EF model to resolve table names and schemas from.</param>
    /// <returns>The complete statement, every identifier schema-qualified and quoted.</returns>
    /// <remarks>Uses <c>CASCADE</c> so a later table with a foreign key into one of these is also emptied.</remarks>
    internal static string BuildTruncateStatement(IModel model) =>
        $"TRUNCATE TABLE {QualifiedIdentifiers(model)} RESTART IDENTITY";

    /// <summary>The five tables as one comma-separated list of schema-qualified, quoted identifiers.</summary>
    private static string QualifiedIdentifiers(IModel model) =>
        string.Join(", ", TablesToClear.Select(clrType => QualifiedTableName(model, clrType)));

    /// <summary>Resolves an entity's schema-qualified, quoted table identifier from EF metadata.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the entity is not mapped to a table, or is mapped somewhere other than
    /// <see cref="CompassSchema"/>.
    /// </exception>
    private static string QualifiedTableName(IModel model, Type clrType)
    {
        var entityType = model.FindEntityType(clrType)
            ?? throw new InvalidOperationException(
                $"{clrType.Name} is not part of the model; Compass data cannot be cleared safely.");

        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException(
                $"{clrType.Name} is not mapped to a table; Compass data cannot be cleared safely.");

        var schema = entityType.GetSchema();
        if (!string.Equals(schema, CompassSchema, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{clrType.Name} is mapped to schema '{schema ?? "public"}', not '{CompassSchema}'. "
                    + "Refusing to clear Compass data against an unexpected schema.");
        }

        return $"\"{schema}\".\"{tableName}\"";
    }
}
