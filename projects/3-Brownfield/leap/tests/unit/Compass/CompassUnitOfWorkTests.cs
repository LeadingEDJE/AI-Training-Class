using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The Compass persistence boundary's translation of a lost uniqueness race, at the unit tier.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists alongside the integration suite of the same name.
/// <c>tests/integration/Compass/CompassUnitOfWorkTests</c> proves the translation against real Postgres,
/// which is the only place a genuine SQLSTATE 23505 can arise — the in-memory provider enforces no unique
/// index. But the backend per-file coverage gate (<c>scripts/check-coverage-ci.sh</c>) measures the UNIT
/// project only, and holds new code to 100%. Without these tests <c>CompassUnitOfWork.cs</c> reported 10%
/// and failed CI while every behavioural test passed. Same-named classes across the two projects is the
/// established convention here — <c>CompassDirectoryRepositoryTests</c> already exists in both.
/// </para>
/// <para>
/// The provider failure is injected by overriding <c>SaveChangesAsync</c> on a context subclass, because
/// the behaviour under test is what the boundary does with the exception, not what raises it.
/// </para>
/// </remarks>
public class CompassUnitOfWorkTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A context whose save fails with a supplied exception.</summary>
    private sealed class FailingContext(
        DbContextOptions<LeapDbContext> options,
        Func<FailingContext, Exception> failure
    ) : LeapDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw failure(this);
    }

    private static FailingContext BuildContext(Func<FailingContext, Exception> failure)
    {
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase($"CompassUnitOfWork_{Guid.NewGuid():N}")
            .Options;
        return new FailingContext(options, failure);
    }

    /// <summary>A context whose save does not fail — for the scope-nesting case, which never saves.</summary>
    /// <summary>
    /// A context that tolerates BeginTransaction, so a scope can reach its commit/rollback decision.
    /// </summary>
    /// <remarks>
    /// The in-memory provider raises <c>TransactionIgnoredWarning</c> as an error by default. Ignoring
    /// it yields a NO-OP transaction: enough for the scope to hold one and choose a branch, and
    /// deliberately not enough to prove anything about what the database did — see the test below.
    /// </remarks>
    private static LeapDbContext BuildTransactionTolerantContext() =>
        new(
            new DbContextOptionsBuilder<LeapDbContext>()
                .UseInMemoryDatabase($"CompassUnitOfWork_{Guid.NewGuid():N}")
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)
                )
                .Options
        );

    private static LeapDbContext BuildPlainContext() =>
        new(
            new DbContextOptionsBuilder<LeapDbContext>()
                .UseInMemoryDatabase($"CompassUnitOfWork_{Guid.NewGuid():N}")
                .Options
        );

    /// <summary>
    /// A unique-violation as the stack really delivers it: Npgsql raises
    /// <see cref="PostgresException"/> and EF Core wraps it in a <see cref="DbUpdateException"/>.
    /// </summary>
    private static DbUpdateException UniqueViolation(IReadOnlyList<EntityEntry> entries) =>
        new(
            "An error occurred while saving the entity changes.",
            new PostgresException(
                messageText: "duplicate key value violates unique constraint",
                severity: "ERROR",
                invariantSeverity: "ERROR",
                sqlState: "23505"
            ),
            entries
        );

    [Fact]
    public async Task SaveChangesAsync_OnAUniqueViolation_ThrowsTheModulesOwnException()
    {
        // Arrange — the losing writer's failure. The service can only answer 409 if the boundary
        // reports the collision in a type the module owns; a raw DbUpdateException escaping here is the
        // bare HTTP 500 that review of PR #213 found.
        using var context = BuildContext(_ => UniqueViolation([]));
        var unitOfWork = new CompassUnitOfWork(context);

        // Act & Assert
        var thrown = await Should.ThrowAsync<CompassDuplicateKeyException>(
            () => unitOfWork.SaveChangesAsync(Token)
        );
        thrown.Message.ShouldNotBeNullOrWhiteSpace();
        thrown.InnerException.ShouldBeOfType<DbUpdateException>();
    }

    [Fact]
    public async Task SaveChangesAsync_OnAUniqueViolation_DetachesWhatTheDatabaseRejected()
    {
        // Arrange — the context is request-scoped and shared, so a rejected INSERT left in Added state
        // would be retried by the next SaveChanges and fail it too, turning one caller's 409 into an
        // unrelated caller's 500. UserRoleService documents the same hazard.
        EntityEntry? tracked = null;
        using var context = BuildContext(failing =>
        {
            tracked = failing.Entry(new EmployeeType { TypeName = "Contract", IsActive = true });
            tracked.State = EntityState.Added;
            return UniqueViolation([tracked]);
        });
        var unitOfWork = new CompassUnitOfWork(context);

        // Act
        await Should.ThrowAsync<CompassDuplicateKeyException>(
            () => unitOfWork.SaveChangesAsync(Token)
        );

        // Assert
        tracked.ShouldNotBeNull();
        tracked!.State.ShouldBe(
            EntityState.Detached,
            "a doomed entity left tracked would poison the next save on this context"
        );
    }

    [Fact]
    public async Task SaveChangesAsync_OnAFailureThatIsNotAUniqueViolation_LetsItPropagate()
    {
        // Arrange — the guard on the guard. Translating every write failure into a duplicate-name
        // conflict would tell an administrator to pick a different name when the real fault was, say, a
        // dropped connection.
        using var context = BuildContext(_ =>
            new DbUpdateException(
                "deadlock detected",
                new PostgresException(
                    messageText: "deadlock detected",
                    severity: "ERROR",
                    invariantSeverity: "ERROR",
                    sqlState: "40P01"
                )
            )
        );
        var unitOfWork = new CompassUnitOfWork(context);

        // Act & Assert
        await Should.ThrowAsync<DbUpdateException>(() => unitOfWork.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task SaveChangesAsync_OnANonDatabaseFailure_LetsItPropagate()
    {
        // Arrange — nothing about a cancelled or invalid operation is a name collision.
        using var context = BuildContext(_ => new InvalidOperationException("the connection dropped"));
        var unitOfWork = new CompassUnitOfWork(context);

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(
            () => unitOfWork.SaveChangesAsync(Token)
        );
    }

    [Fact]
    public async Task SaveChangesAsync_WhenNothingFails_ReturnsTheWrittenCount()
    {
        // Arrange — the positive control. Every assertion above is about a throw, so without this the
        // boundary could fail every save and they would all still pass.
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase($"CompassUnitOfWork_{Guid.NewGuid():N}")
            .Options;
        using var context = new LeapDbContext(options);
        context.Set<EmployeeType>().Add(new EmployeeType { TypeName = "Contract", IsActive = true });
        var unitOfWork = new CompassUnitOfWork(context);

        // Act
        var written = await unitOfWork.SaveChangesAsync(Token);

        // Assert
        written.ShouldBe(1);
    }

    // --------------------------------------------------------------- ExecuteAtomicallyAsync: nesting
    //
    // Neither this class nor its integration namesake exercised ExecuteAtomicallyAsync at all before
    // this: its coverage came only incidentally, through the services that call it. The join path is
    // the half reachable without a transaction, because a nested call returns before any statement
    // runs — which is precisely the behaviour being asserted.

    /// <summary>
    /// A nested scope runs its operation and returns, rather than opening a second scope of its own.
    /// </summary>
    /// <remarks>
    /// Npgsql rejects a nested <c>BeginTransaction</c>, so a scope that did not join would throw the
    /// moment two Compass services composed. The outer scope owns commit and rollback; the inner one
    /// must not.
    /// </remarks>
    [Fact]
    public async Task ExecuteAtomicallyAsync_NestedInsideAnotherScope_JoinsRatherThanOpeningASecond()
    {
        // Arrange — no save anywhere, so no transaction is ever begun and the in-memory provider is
        // never asked for one.
        var context = BuildPlainContext();
        var unitOfWork = new CompassUnitOfWork(context);
        var innerRuns = 0;

        // Act
        var result = await unitOfWork.ExecuteAtomicallyAsync(
            async outerToken =>
                await unitOfWork.ExecuteAtomicallyAsync(
                    _ =>
                    {
                        innerRuns++;
                        return Task.FromResult("inner result");
                    },
                    _ => true,
                    outerToken
                ),
            _ => true,
            Token
        );

        // Assert — the inner operation ran exactly once and its value is what came back, so the join
        // path returns the operation's result rather than swallowing or re-running it.
        result.ShouldBe("inner result");
        innerRuns.ShouldBe(1);
    }

    /// <summary>
    /// The nested call joins even when the inner scope would NOT have committed on its own.
    /// </summary>
    /// <remarks>
    /// The inner <c>commitWhen</c> is deliberately <c>false</c>. A nested scope that honoured its own
    /// predicate would decide the fate of statements the OUTER scope owns — the failure this join
    /// exists to prevent — and the inner result must still be handed back untouched.
    /// </remarks>
    [Fact]
    public async Task ExecuteAtomicallyAsync_NestedWithARefusingPredicate_StillReturnsTheInnerResult()
    {
        // Arrange
        var context = BuildPlainContext();
        var unitOfWork = new CompassUnitOfWork(context);

        // Act
        var result = await unitOfWork.ExecuteAtomicallyAsync(
            async outerToken =>
                await unitOfWork.ExecuteAtomicallyAsync(
                    _ => Task.FromResult("refused inner"),
                    _ => false,
                    outerToken
                ),
            _ => true,
            Token
        );

        // Assert
        result.ShouldBe("refused inner");
    }

    /// <summary>
    /// A refusal that had already saved takes the ROLLBACK branch, and its result still comes back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The transaction opens on the first save, not on scope entry, so a refusal only reaches this
    /// branch if it staged something first. Postgres treats a refusal arriving through a FAILED
    /// statement as having aborted the transaction, where COMMIT would throw rather than quietly
    /// persist nothing — hence rollback.
    /// </para>
    /// <para>
    /// What this test does NOT prove. The in-memory transaction is a no-op, so this asserts
    /// only that the refusal takes the branch and passes its result through without throwing. That the
    /// DATABASE discarded the staged write is a Postgres property, belongs to
    /// <c>tests/integration/Compass/CompassUnitOfWorkTests</c>, and is not asserted there yet. This
    /// test exists because the per-file coverage gate reads the unit project alone — the same reason
    /// this whole class exists, per the class remark above.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ExecuteAtomicallyAsync_WhenThePredicateRefusesAfterASave_ReturnsTheResult()
    {
        // Arrange
        var context = BuildTransactionTolerantContext();
        var unitOfWork = new CompassUnitOfWork(context);

        // Act — the operation saves (opening the scope transaction) and then reports a refusal, so
        // commitWhen answers false and the scope must roll back rather than commit.
        var result = await unitOfWork.ExecuteAtomicallyAsync(
            async operationToken =>
            {
                await unitOfWork.SaveChangesAsync(operationToken);
                return "refused";
            },
            _ => false,
            Token
        );

        // Assert
        result.ShouldBe("refused");
    }

    /// <summary>The committing counterpart, so both branches are driven from the same shape.</summary>
    [Fact]
    public async Task ExecuteAtomicallyAsync_WhenThePredicateAcceptsAfterASave_ReturnsTheResult()
    {
        // Arrange
        var context = BuildTransactionTolerantContext();
        var unitOfWork = new CompassUnitOfWork(context);

        // Act
        var result = await unitOfWork.ExecuteAtomicallyAsync(
            async operationToken =>
            {
                await unitOfWork.SaveChangesAsync(operationToken);
                return "accepted";
            },
            _ => true,
            Token
        );

        // Assert
        result.ShouldBe("accepted");
    }
}
