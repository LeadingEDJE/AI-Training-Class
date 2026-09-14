using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Configurations;

/// <summary>
/// Base configuration for every Compass entity: maps the entity to its ERD table name inside the
/// <c>compass</c> Postgres schema.
/// </summary>
/// <typeparam name="TEntity">The Compass entity type.</typeparam>
/// <remarks>
/// The schema is applied here, not per entity: repeating <c>ToTable(name, "compass")</c> in seven
/// files is seven chances to typo it, and a typo does not fail — it silently creates the table in
/// <c>public</c> beside the timesheet tables. Table and column names are explicit throughout, so
/// Compass is immune to the rename-an-entity-renames-a-table accident that derived names allow.
///
/// Columns are explicit in each derived configuration because the ERD is a published contract and the
/// convention's trailing-digit handling is not obvious: <c>CanSubmitUnder40</c> does not reliably
/// yield <c>can_submit_under_40</c>, and <c>CompassSchemaFromErdTests.CompassColumns_AreSnakeCase</c>
/// checks the live catalog. Compass owns no <c>DbSet</c>; entities come from <c>context.Set&lt;T&gt;()</c>.
/// </remarks>
public abstract class CompassEntityConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : class
{
    /// <summary>The Postgres schema every Compass table lives in.</summary>
    protected const string Schema = "compass";

    /// <summary>The ERD table name for <typeparamref name="TEntity"/>, without the schema.</summary>
    protected abstract string TableName { get; }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        // Table-level constraints (CHECKs) must be declared through the table builder, which is why
        // ToTable takes the hook rather than being called bare and then again by a derived class.
        builder.ToTable(TableName, Schema, ConfigureTable);
        ConfigureEntity(builder);
    }

    /// <summary>Configures keys, columns, relationships and indexes for this entity.</summary>
    protected abstract void ConfigureEntity(EntityTypeBuilder<TEntity> builder);

    /// <summary>
    /// Adds table-level constraints. Override to declare CHECK constraints; the default adds none.
    /// </summary>
    /// <remarks>
    /// Exclusion constraints (the SOW non-overlap rule) cannot be expressed here — EF Core has no
    /// fluent API for <c>EXCLUDE USING gist</c>, so that one is raw SQL in the migration. It is
    /// nonetheless verified against the live catalog by the integration tests, not trusted to intent.
    /// </remarks>
    protected virtual void ConfigureTable(TableBuilder<TEntity> table) { }
}
