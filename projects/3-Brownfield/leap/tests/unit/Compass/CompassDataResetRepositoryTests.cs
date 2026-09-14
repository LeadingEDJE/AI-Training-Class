using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The SQL the Compass clear issues, and the counts it takes first.
/// </summary>
/// <remarks>
/// <para>
/// Statement SHAPE is provider-agnostic and belongs here; whether the statement RUNS is a question
/// only real PostgreSQL can answer. That split is the same one
/// <c>PostgresSequenceSync.BuildResyncStatements</c> makes, and it is why the truncate text is built
/// by a pure static method rather than inline. The execution half lives in
/// <c>tests/integration/Endpoints/CompassDeveloperToolsEndpointsTests.cs</c>.
/// </para>
/// <para>
/// The counts, by contrast, are ordinary LINQ and run perfectly well on the InMemory provider, so they
/// are asserted here against a real repository instance rather than through a double.
/// </para>
/// </remarks>
public class CompassDataResetRepositoryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static LeapDbContext NewContext() =>
        new(new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase($"CompassClear_{Guid.NewGuid():N}")
            .Options);

    /// <summary>Seeds one row in each of the five cleared tables plus one in each lookup table.</summary>
    private static async Task SeedAsync(LeapDbContext context)
    {
        context.Add(new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true });
        context.Add(new InvoiceFrequencyType { Id = 1, TypeName = "Monthly", IsActive = true });
        context.Add(new Client { Id = 1, ClientName = "Acme", InvoiceFrequencyTypeId = 1 });
        context.Add(new Employee
        {
            Id = 1,
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@example.test",
            EmployeeTypeId = 1,
            HireDate = new DateOnly(2020, 1, 6),
            StateOfResidence = "OH",
            IsActive = true,
        });
        context.Add(new ClientAssignment
        {
            Id = 1,
            EmployeeId = 1,
            ClientId = 1,
            StartDate = new DateOnly(2024, 1, 1),
        });
        context.Add(new Sow
        {
            Id = 1,
            ClientAssignmentId = 1,
            SowStartDate = new DateOnly(2024, 1, 1),
            SowEndDate = new DateOnly(2024, 12, 31),
        });
        context.Add(new BillableTimeCategory { Id = 1, ClientId = 1, CategoryName = "Development" });
        context.Add(new BillableTimeCategory { Id = 2, ClientId = 1, CategoryName = "Support" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void BuildTruncateStatement_NamesAllFiveTables_SchemaQualifiedAndQuoted()
    {
        // Arrange — every identifier must be schema-qualified: the application connection pins
        // search_path to `public` (issue #208), so an unqualified `employee` resolves to a table in the
        // wrong schema or to nothing at all.
        using var context = NewContext();

        // Act
        var sql = CompassDataResetRepository.BuildTruncateStatement(context.Model);

        // Assert
        sql.ShouldBe(
            "TRUNCATE TABLE \"compass\".\"billable_time_category\", \"compass\".\"sow\", "
                + "\"compass\".\"client_assignment\", \"compass\".\"employee\", "
                + "\"compass\".\"client\" RESTART IDENTITY");
    }

    [Fact]
    public void BuildTruncateStatement_OmitsCascade_SoANewReferencingTableFailsLoudly()
    {
        // Arrange — CASCADE is the obvious "fix" the first time someone adds a Compass table with a
        // foreign key into one of these five and hits an FK error. It would also silently empty their
        // new table AND both lookup tables, which are FK parents here. Naming all five instead
        // satisfies every FK between them (including employee's self-reference through the coach FK)
        // while leaving anything outside the list to fail the statement rather than be destroyed.
        using var context = NewContext();

        // Act
        var sql = CompassDataResetRepository.BuildTruncateStatement(context.Model);

        // Assert
        sql.ShouldNotContain("CASCADE");
        sql.ShouldContain("RESTART IDENTITY");
    }

    [Fact]
    public void BuildTruncateStatement_MentionsNeitherLookupTable()
    {
        // Arrange
        using var context = NewContext();

        // Act
        var sql = CompassDataResetRepository.BuildTruncateStatement(context.Model);

        // Assert — asserted on the rendered SQL, not only on the type list, because the SQL is what
        // reaches the database.
        sql.ShouldNotContain("employee_type");
        sql.ShouldNotContain("invoice_frequency_type");
    }

    [Fact]
    public void TablesToClear_IsExactlyTheFiveOperationalTables()
    {
        // Arrange / Act — a pinning test. The list is the sole input to the generated TRUNCATE, so a
        // change to it is a change to what gets destroyed.
        var tables = CompassDataResetRepository.TablesToClear;

        // Assert — order is pinned, not just the set, so the child-first reading stays legible.
        tables.ShouldBe(
        [
            typeof(BillableTimeCategory),
            typeof(Sow),
            typeof(ClientAssignment),
            typeof(Employee),
            typeof(Client),
        ]);

        // And explicitly NOT the lookup tables — stated as an assertion so adding one fails here first.
        tables.ShouldNotContain(typeof(EmployeeType));
        tables.ShouldNotContain(typeof(InvoiceFrequencyType));
    }

    // ---------------------------------------------------------------- the model guards
    //
    // These three refusals stand between a TRUNCATE and the wrong table, so leaving them unexecuted
    // would be the worst place in this feature to have never-run code: a mistyped interpolation or an
    // inverted comparison in a guard reads exactly like a working guard. Each is driven by handing
    // BuildTruncateStatement a deliberately wrong model — which is possible only because it takes an
    // IModel rather than reaching into a context, and is a large part of why it does.

    /// <summary>A model that knows nothing about Compass at all.</summary>
    private sealed class UnrelatedModelContext(DbContextOptions<UnrelatedModelContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<UnrelatedRow>().ToTable("unrelated");
    }

    private sealed class UnrelatedRow
    {
        public int Id { get; set; }
    }

    /// <summary>A model that maps a Compass entity to <c>public</c> instead of <c>compass</c>.</summary>
    private sealed class WrongSchemaContext(DbContextOptions<WrongSchemaContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<BillableTimeCategory>(entity =>
            {
                entity.Ignore(x => x.Client);
                entity.ToTable("billable_time_category");
            });
    }

    /// <summary>A model in which a Compass entity is mapped to no table at all.</summary>
    private sealed class NoTableContext(DbContextOptions<NoTableContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<BillableTimeCategory>(entity =>
            {
                entity.Ignore(x => x.Client);
                entity.ToTable((string?)null);
            });
    }

    private static TContext NewContext<TContext>() where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseInMemoryDatabase($"CompassClearGuard_{Guid.NewGuid():N}")
            .Options;
        return (TContext)Activator.CreateInstance(typeof(TContext), options)!;
    }

    [Fact]
    public void BuildTruncateStatement_WhenAnEntityIsNotInTheModel_Refuses()
    {
        // Arrange
        using var context = NewContext<UnrelatedModelContext>();

        // Act / Assert — refusing beats emitting a TRUNCATE with a hole in it.
        var error = Should.Throw<InvalidOperationException>(
            () => CompassDataResetRepository.BuildTruncateStatement(context.Model));
        error.Message.ShouldContain(nameof(BillableTimeCategory));
        error.Message.ShouldContain("not part of the model");
    }

    [Fact]
    public void BuildTruncateStatement_WhenAnEntityIsMappedToNoTable_Refuses()
    {
        // Arrange
        using var context = NewContext<NoTableContext>();

        // Act / Assert
        var error = Should.Throw<InvalidOperationException>(
            () => CompassDataResetRepository.BuildTruncateStatement(context.Model));
        error.Message.ShouldContain("not mapped to a table");
    }

    [Fact]
    public void BuildTruncateStatement_WhenAnEntityMovedOutOfTheCompassSchema_Refuses()
    {
        // Arrange — the guard that matters most. A model change that quietly relocated a Compass entity
        // to `public` would otherwise point this TRUNCATE at a table in the shared schema, which is
        // where the frozen legacy directory tables live.
        using var context = NewContext<WrongSchemaContext>();

        // Act / Assert
        var error = Should.Throw<InvalidOperationException>(
            () => CompassDataResetRepository.BuildTruncateStatement(context.Model));
        error.Message.ShouldContain("'public'");
        error.Message.ShouldContain("Refusing to clear Compass data");
    }

    [Fact]
    public void BuildLockStatement_LocksAllFiveTablesInAccessExclusiveMode()
    {
        // Arrange — this statement is what makes the reported counts true. Counting and truncating are
        // two statements, and under READ COMMITTED each takes its own snapshot, so without holding the
        // truncate's own lock across both a concurrent insert is destroyed without ever being counted
        // — and the audit entry, the only surviving record of what was lost, understates it.
        using var context = NewContext();

        // Act
        var sql = CompassDataResetRepository.BuildLockStatement(context.Model);

        // Assert
        sql.ShouldBe(
            "LOCK TABLE \"compass\".\"billable_time_category\", \"compass\".\"sow\", "
                + "\"compass\".\"client_assignment\", \"compass\".\"employee\", "
                + "\"compass\".\"client\" IN ACCESS EXCLUSIVE MODE");
    }

    [Fact]
    public void BuildLockStatement_LocksExactlyTheTablesTheTruncateEmpties()
    {
        // Arrange — the lock and the truncate naming different sets would silently reopen the window
        // the lock exists to close: a table locked but not truncated protects nothing, and a table
        // truncated but not locked is the original bug. They share one identifier renderer, and this
        // is what holds that together if someone splits them again.
        using var context = NewContext();

        // Act
        var lockSql = CompassDataResetRepository.BuildLockStatement(context.Model);
        var truncateSql = CompassDataResetRepository.BuildTruncateStatement(context.Model);

        // Assert
        var lockedTables = lockSql
            .Replace("LOCK TABLE ", string.Empty, StringComparison.Ordinal)
            .Replace(" IN ACCESS EXCLUSIVE MODE", string.Empty, StringComparison.Ordinal);
        var truncatedTables = truncateSql
            .Replace("TRUNCATE TABLE ", string.Empty, StringComparison.Ordinal)
            .Replace(" RESTART IDENTITY", string.Empty, StringComparison.Ordinal);

        lockedTables.ShouldBe(truncatedTables);
    }

    [Fact]
    public async Task CountAsync_ReportsEachTableSeparately()
    {
        // Arrange — two billable time categories and one of everything else, so a transposed pair of
        // counts cannot pass.
        using var context = NewContext();
        await SeedAsync(context);
        var repository = new CompassDataResetRepository(context);

        // Act
        var counts = await repository.CountAsync(Token);

        // Assert
        counts.BillableTimeCategories.ShouldBe(2);
        counts.Sows.ShouldBe(1);
        counts.ClientAssignments.ShouldBe(1);
        counts.Employees.ShouldBe(1);
        counts.Clients.ShouldBe(1);
        counts.Total.ShouldBe(6);
    }

    [Fact]
    public async Task CountAsync_OnAnEmptyDatabase_IsAllZeroes()
    {
        // Arrange
        using var context = NewContext();
        var repository = new CompassDataResetRepository(context);

        // Act
        var counts = await repository.CountAsync(Token);

        // Assert
        counts.Total.ShouldBe(0);
    }

    [Fact]
    public async Task ClearAsync_RequiresARelationalProvider()
    {
        // Arrange — pins that the clear is REAL SQL and has no in-memory equivalent: it opens a
        // transaction, takes a lock and issues a TRUNCATE, none of which the InMemory provider has.
        // That is why the service is unit-tested against a double of this interface and why the
        // behaviour of the clear itself is asserted only against PostgreSQL
        // (tests/integration/Endpoints/CompassDeveloperToolsEndpointsTests.cs). If this ever stops
        // throwing, someone has added a silent non-relational fallback, and the unit suite would then
        // appear to cover a path production never takes.
        using var context = NewContext();
        var repository = new CompassDataResetRepository(context);

        // Act / Assert
        await Should.ThrowAsync<InvalidOperationException>(() => repository.ClearAsync(Token));
    }
}
