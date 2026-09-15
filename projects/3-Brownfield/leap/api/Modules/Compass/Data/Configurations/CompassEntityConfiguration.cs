using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Configurations;

/// <summary>
/// Base configuration for every Compass entity. Column names follow EF's default convention.
/// </summary>
/// <typeparam name="TEntity">The Compass entity type.</typeparam>
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
        builder.ToTable(TableName, Schema, ConfigureTable);
        ConfigureEntity(builder);
    }

    /// <summary>Configures keys, columns, relationships and indexes for this entity.</summary>
    protected abstract void ConfigureEntity(EntityTypeBuilder<TEntity> builder);

    /// <summary>Adds table-level constraints, including exclusion constraints.</summary>
    protected virtual void ConfigureTable(TableBuilder<TEntity> table) { }
}
