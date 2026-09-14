using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
// Compass's own entities. Aliased rather than imported bare: this file sits in a namespace ending
// `.Compass`, so an unqualified `using LeadingEDJE.Leap.Api.Modules.Compass;` reads as though it
// referred to the test namespace itself.
using CompassEmployee = LeadingEDJE.Leap.Api.Modules.Compass.Employee;
using CompassEmployeeType = LeadingEDJE.Leap.Api.Modules.Compass.EmployeeType;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// Whether a Compass audit entry actually co-commits with the change it describes (spec 002 T061,
/// FR-043 / SC-007).
/// </summary>
/// <remarks>
/// <para>
/// Why this can only be an integration test. The property under test is transactional: does the
/// business change survive when the audit write fails? The EF Core InMemory provider has no
/// transactions at all, so it answers "yes" to every arrangement and would report this as passing
/// whatever the services do.
/// </para>
/// <para>
/// Why failure has to be injected rather than provoked. Nothing a caller supplies can make the
/// audit INSERT fail on its own — <c>LogAsync</c> validates the reason before staging anything, and
/// every other column it writes is service-controlled. So the failure is injected at the one moment
/// that matters, the <c>SaveChanges</c> carrying the <see cref="AuditLog"/>, by an interceptor. That
/// is the same instant a constraint violation, a lost connection or a process death would land.
/// </para>
/// </remarks>
public class CompassAuditAtomicityTests(IntegrationTestFactory factory) : IntegrationTestBase(factory)
{
    private readonly IntegrationTestFactory _factory = factory;

    private const string Route = "/api/compass/v1/admin/edjers";
    private const int EmployeeTypeId = 1;
    private const string Email = "atomicity.probe@example.test";

    /// <summary>
    /// Fails the <c>SaveChanges</c> that carries an <see cref="AuditLog"/>, and only that one, so the
    /// change-carrying save ahead of it is untouched.
    /// </summary>
    private sealed class FailTheAuditWriteInterceptor : SaveChangesInterceptor
    {
        internal const string FailureMessage = "Injected: the audit_logs INSERT failed.";

        /// <summary>True once the audit-carrying save was seen, so a test can prove it was reached.</summary>
        internal bool AuditWriteWasAttempted { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var carriesAuditRow = eventData.Context is not null
                && eventData.Context.ChangeTracker
                    .Entries<AuditLog>()
                    .Any(entry => entry.State == EntityState.Added);

            if (carriesAuditRow)
            {
                AuditWriteWasAttempted = true;
                throw new InvalidOperationException(FailureMessage);
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>Counts the transactions actually opened, so "opened none" is provable.</summary>
    private sealed class CountTransactionsInterceptor : DbTransactionInterceptor
    {
        internal int Opened { get; private set; }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            Opened++;
            return base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
        }
    }

    /// <summary>A host against the SAME database whose context counts transactions.</summary>
    private WebApplicationFactory<Program> CreateHostCountingTransactions(
        CountTransactionsInterceptor interceptor) => ReplaceContext(interceptor);

    /// <summary>
    /// A host against the SAME database as the shared factory, whose context fails the audit write.
    /// Re-registering the context is what lets the interceptor be attached; nothing else changes.
    /// </summary>
    private WebApplicationFactory<Program> CreateHostThatFailsTheAuditWrite(
        FailTheAuditWriteInterceptor interceptor) => ReplaceContext(interceptor);

    private WebApplicationFactory<Program> ReplaceContext(IInterceptor interceptor)
    {
        return _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                var connectionString = ConnectionStringOfSharedDatabase();

                var contextDescriptors = services
                    .Where(d => d.ServiceType == typeof(DbContextOptions<LeapDbContext>)
                        || d.ServiceType == typeof(DbContextOptions))
                    .ToList();
                foreach (var descriptor in contextDescriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<LeapDbContext>(options => options
                    .UseNpgsql(connectionString)
                    .UseSnakeCaseNamingConvention()
                    .AddInterceptors(interceptor));
            }));
    }

    private string ConnectionStringOfSharedDatabase()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        return context.Database.GetConnectionString()!;
    }

    private async Task SeedEmployeeTypeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (!await db.Set<CompassEmployeeType>().AnyAsync(TestContext.Current.CancellationToken))
        {
            db.Set<CompassEmployeeType>().Add(new CompassEmployeeType
            {
                Id = EmployeeTypeId,
                TypeName = "Full Time",
                IsActive = true,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    private static CompassEdjerRequest ValidRequest() => new(
        FirstName: "Grace",
        LastName: "Hopper",
        HireDate: new DateOnly(2020, 1, 1),
        Email: Email,
        EmployeeTypeId: EmployeeTypeId,
        CoachEmployeeId: null,
        StateOfResidence: "OH",
        IsActive: true,
        TimesheetRequired: true,
        CanSubmitUnder40: false,
        IncludeInPayroll: true);

    [Fact]
    public async Task WhenTheAuditWriteFails_TheChangeItDescribesIsNotCommittedEither()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedEmployeeTypeAsync();
        var interceptor = new FailTheAuditWriteInterceptor();
        using var host = CreateHostThatFailsTheAuditWrite(interceptor);

        // Act — create an EDJEr whose audit entry cannot be written. Driven over HTTP because
        // ICurrentUserContext reads the request's principal: a bare DI scope has no HTTP context, so
        // calling the service directly dies at `Actor` before reaching the write at all.
        // Compass grants nothing to timesheet's roles (Principle IV: every module owns its own roles
        // and inherits nothing), so the default test principal is refused every Compass write with 403.
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.PrivilegeOverrideHeader, RolePolicy.CompassSuperAdminRole);

        var response = await client.PostAsJsonAsync(
            Route, ValidRequest(), TestContext.Current.CancellationToken);

        // Assert — the injection landed where it was aimed, so the rest of this test means something.
        response.StatusCode.ShouldBe(
            HttpStatusCode.InternalServerError,
            "the injected audit failure should surface as an unhandled write failure");
        interceptor.AuditWriteWasAttempted.ShouldBeTrue(
            "the audit write was never reached, so this test proves nothing about atomicity");

        // ...and the EDJEr must not survive an unauditable write. Read on a separate context so this
        // sees committed state rather than anything still tracked by the writing context.
        using var verificationScope = Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var survivors = await db.Set<CompassEmployee>()
            .Where(employee => employee.Email == Email)
            .CountAsync(TestContext.Current.CancellationToken);

        survivors.ShouldBe(
            0,
            "the EDJEr committed in a transaction of its own, so a change now exists that no audit "
            + "entry describes — SC-007 requires 100% of writes to produce one");

        var auditRows = await db.Set<AuditLog>()
            .CountAsync(TestContext.Current.CancellationToken);
        auditRows.ShouldBe(0, "no audit row can exist: its write is what failed");
    }

    [Fact]
    public async Task AnAuditedWrite_CommitsBothTheChangeAndItsEntry()
    {
        // Arrange — the positive control. Without it, a service that wrote NOTHING would satisfy the
        // test above, and this suite would call that atomicity.
        await ResetDatabaseAsync();
        await SeedEmployeeTypeAsync();

        // Act
        using var client = _factory.AsCompassSuperAdmin();
        var response = await client.PostAsJsonAsync(
            Route, ValidRequest(), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var verificationScope = Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var employees = await db.Set<CompassEmployee>()
            .Where(employee => employee.Email == Email)
            .CountAsync(TestContext.Current.CancellationToken);
        employees.ShouldBe(1);

        var entries = await db.Set<AuditLog>()
            .Where(entry => entry.EntityType == "CompassEmployee")
            .ToListAsync(TestContext.Current.CancellationToken);
        entries.Count.ShouldBe(1);
        entries[0].Action.ShouldBe("create");
        entries[0].EffectiveRoles.ShouldNotBeNull();
    }

    [Fact]
    public async Task AWriteRefusedByValidation_OpensNoTransactionAtAll()
    {
        // Arrange — the reviewer's nit on PR #288: the first cut opened a transaction when the scope
        // opened, so every validation-only rejection paid a BEGIN + ROLLBACK round trip for a path that
        // touches no data. The transaction now opens on the first SAVE, and this pins that.
        await ResetDatabaseAsync();
        await SeedEmployeeTypeAsync();
        var counter = new CountTransactionsInterceptor();
        using var host = CreateHostCountingTransactions(counter);

        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.PrivilegeOverrideHeader, RolePolicy.CompassSuperAdminRole);

        // Act — an employee type that does not exist is refused by validation, before anything is staged.
        var refused = ValidRequest() with { EmployeeTypeId = 999_999 };
        var response = await client.PostAsJsonAsync(
            Route, refused, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "the arrangement must actually be refused");
        counter.Opened.ShouldBe(
            0,
            "a rejection that stages nothing must not open a transaction — it has nothing to commit");
    }

    [Fact]
    public async Task AnAcceptedWrite_OpensExactlyOneTransaction()
    {
        // Arrange — the other half: lazily opening must not mean never opening, and the change plus its
        // audit entry must share ONE transaction rather than taking one each.
        await ResetDatabaseAsync();
        await SeedEmployeeTypeAsync();
        var counter = new CountTransactionsInterceptor();
        using var host = CreateHostCountingTransactions(counter);

        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.PrivilegeOverrideHeader, RolePolicy.CompassSuperAdminRole);

        // Act
        var response = await client.PostAsJsonAsync(
            Route, ValidRequest(), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        counter.Opened.ShouldBe(
            1,
            "the change and its audit entry are two saves that must share one transaction");
    }
}
