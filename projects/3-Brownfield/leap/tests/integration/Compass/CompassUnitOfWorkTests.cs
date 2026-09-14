using LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// The Compass persistence boundary against real PostgreSQL: what it does when the database rejects a
/// write.
/// </summary>
/// <remarks>
/// <para>
/// Only assertable against Postgres. The in-memory provider enforces no unique index, so nothing
/// there ever raises SQLSTATE 23505 and the translation under test could never fire.
/// </para>
/// <para>
/// Why the boundary owns the translation rather than the service: rule two of
/// <c>CompassBoundaryTests</c> fails the build if any file under the module's <c>Services/</c> folder
/// contains the token <c>DbContext</c>, so a Compass service cannot catch a provider exception. See
/// <see cref="CompassDuplicateKeyException"/>.
/// </para>
/// </remarks>
public class CompassUnitOfWorkTests(IntegrationTestFactory factory) : IntegrationTestBase(factory)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task TheUnitOfWork_TranslatesARealUniqueViolation_IntoTheModulesOwnException()
    {
        // Arrange — the losing writer's exact failure, forced deterministically rather than by racing.
        // Every uniqueness guard in this module is a check-then-act, so a concurrent caller can commit
        // the same name between the check and the write; Postgres then rejects the loser with SQLSTATE
        // 23505. Unhandled, that reached the caller as a bare HTTP 500 (review of PR #213).
        //
        // This is the assertion the in-memory provider cannot make at all: it enforces no unique index,
        // so nothing there ever raises 23505.
        await ResetDatabaseAsync();
        var name = UniqueName("Raced");

        using var seedScope = Services.CreateScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<LeapDbContext>();
        seedDb.Set<EmployeeType>().Add(new EmployeeType { TypeName = name, IsActive = true });
        await seedDb.SaveChangesAsync(Token);

        // Act — a second row with the same name, saved through the module's own boundary. Both the
        // context and the unit of work come from ONE scope, so this is the same tracked change set the
        // service would have staged.
        using var raceScope = Services.CreateScope();
        var raceDb = raceScope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var unitOfWork = raceScope.ServiceProvider.GetRequiredService<ICompassUnitOfWork>();
        raceDb.Set<EmployeeType>().Add(new EmployeeType { TypeName = name, IsActive = true });

        // Assert — the module's own exception, NOT the provider's DbUpdateException.
        var thrown = await Should.ThrowAsync<CompassDuplicateKeyException>(
            () => unitOfWork.SaveChangesAsync(Token)
        );
        thrown.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TheUnitOfWork_LeavesNoDoomedEntityTracked_AfterARejectedWrite()
    {
        // Arrange — the context is request-scoped and shared with anything else running in that
        // request, so a rejected INSERT left in Added state would be retried by the next save and fail
        // it too. UserRoleService documents the same hazard and detaches for the same reason.
        await ResetDatabaseAsync();
        var name = UniqueName("Doomed");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<ICompassUnitOfWork>();
        db.Set<EmployeeType>().Add(new EmployeeType { TypeName = name, IsActive = true });
        await db.SaveChangesAsync(Token);
        db.Set<EmployeeType>().Add(new EmployeeType { TypeName = name, IsActive = true });
        await Should.ThrowAsync<CompassDuplicateKeyException>(
            () => unitOfWork.SaveChangesAsync(Token)
        );

        // Act — an unrelated, valid write on the SAME context must now succeed.
        db.Set<EmployeeType>().Add(new EmployeeType { TypeName = UniqueName("After"), IsActive = true });

        // Assert
        await unitOfWork.SaveChangesAsync(Token);
    }
}
