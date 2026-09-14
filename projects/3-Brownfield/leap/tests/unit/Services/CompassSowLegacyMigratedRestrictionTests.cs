using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using NSubstitute;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// The validation bypass carried by <see cref="SowType.LegacyMigrated"/>, and who may reach it.
/// </summary>
/// <remarks>
/// <para>
/// What the exemption actually is. Two of the three database rules on <c>compass.sow</c> are
/// PARTIAL:
/// </para>
/// <code>
/// sow_type = 'LegacyMigrated' OR sow_end_date &gt;= sow_start_date      -- CHECK, partial
/// EXCLUDE USING gist (...) WHERE (sow_type &lt;&gt; 'LegacyMigrated')       -- overlap, partial
/// </code>
/// <para>
/// So a <c>LegacyMigrated</c> row may carry a backwards date range AND overlap a sibling. That is
/// correct for a faithful TPS load and catastrophic as a general capability.
/// </para>
/// <para>
/// Constitution Principle VIII, verbatim: "Any validation-bypass path (for example, migration
/// grandfathering under Compass BR-14) MUST be reachable only by the migration principal and never
/// through the application surface, or the exemption becomes a general-purpose way around
/// validation."
/// </para>
/// <para>
/// Gated on principal IDENTITY, not on a role. The migration principal holds the Compass root
/// role, so a role-based check would hand this bypass to every Compass Super Admin. These two tests
/// are required deliverables of research R5 — without them the rule is a MUST with no gate, which
/// Governance says "always passes and reads as compliance".
/// </para>
/// </remarks>
public class CompassSowLegacyMigratedRestrictionTests
{
    private readonly ICompassSowRepository _repository = Substitute.For<ICompassSowRepository>();
    private readonly ICompassUnitOfWork _unitOfWork = Substitute.For<ICompassUnitOfWork>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly ICurrentUserContext _currentUser = Substitute.For<ICurrentUserContext>();

    private CompassSowMigrationService CreateService(Guid actor)
    {
        _currentUser.EdjeId.Returns(actor);
        _repository
            .AssignmentExistsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return new CompassSowMigrationService(_repository, _unitOfWork, _audit, _currentUser);
    }

    private static CompassSowRequest Request(SowType type) =>
        new(
            ClientAssignmentId: 1,
            SowType: type,
            RateIncrease: false,
            SowStartDate: new DateOnly(2024, 1, 1),
            SowEndDate: new DateOnly(2024, 12, 31),
            Note: null
        );

    [Fact]
    public async Task ACompassSuperAdmin_IsRefused_LegacyMigrated()
    {
        // Arrange — a human holding the Compass root. Highest authority the application offers.
        var service = CreateService(Guid.NewGuid());

        // Act
        var result = await service.CreateAsync(
            Request(SowType.LegacyMigrated),
            isMigrationPrincipal: false,
            TestContext.Current.CancellationToken
        );

        // Assert — the exact status, never "not success". A loose assertion here would pass under a
        // misconfigured environment and the gate would vanish silently.
        result.Status.ShouldBe(CompassWriteStatus.Forbidden);
    }

    [Fact]
    public async Task ACompassSuperAdmin_IsRefused_BeforeAnythingIsWritten()
    {
        // Arrange
        var service = CreateService(Guid.NewGuid());

        // Act
        await service.CreateAsync(
            Request(SowType.LegacyMigrated),
            isMigrationPrincipal: false,
            TestContext.Current.CancellationToken
        );

        // Assert — a refusal that still saved would defeat the purpose entirely.
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SowType.InitialContract)]
    [InlineData(SowType.SowExtension)]
    public async Task ACompassSuperAdmin_IsPermitted_EveryOtherType(SowType type)
    {
        // Arrange — the other half of the rule. Restricting LegacyMigrated must not restrict the
        // types the application legitimately creates.
        var service = CreateService(Guid.NewGuid());

        // Act
        var result = await service.CreateAsync(
            Request(type),
            isMigrationPrincipal: false,
            TestContext.Current.CancellationToken
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
    }

    [Fact]
    public async Task TheMigrationPrincipal_IsPermitted_LegacyMigrated()
    {
        // Arrange
        var service = CreateService(MigrationPrincipal.EdjeId);

        // Act
        var result = await service.CreateAsync(
            Request(SowType.LegacyMigrated),
            isMigrationPrincipal: true,
            TestContext.Current.CancellationToken
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
    }

    [Fact]
    public async Task AMigratedPeriod_StartsUnvalidated_SoItsFirstEditMustSatisfyBothRules()
    {
        // Arrange
        var service = CreateService(MigrationPrincipal.EdjeId);

        // Act
        var result = await service.CreateAsync(
            Request(SowType.LegacyMigrated),
            isMigrationPrincipal: true,
            TestContext.Current.CancellationToken
        );

        // Assert — false is the only honest value: the row was admitted WITHOUT satisfying the
        // overlap and date-order rules. Defaulting it true would let a row assert a check it never
        // passed, and the exemption would become permanent rather than load-time.
        result.Value!.HasPassedApplicationValidation.ShouldBeFalse();
    }

    [Fact]
    public async Task TheMigrationPrincipal_MayLoadABackwardsDateRange()
    {
        // Arrange — legacy TPS contains ranges no application would accept. Admitting them exactly as
        // recorded is the whole reason the CHECK is partial.
        var service = CreateService(MigrationPrincipal.EdjeId);

        // Act
        var result = await service.CreateAsync(
            new CompassSowRequest(
                ClientAssignmentId: 1,
                SowType: SowType.LegacyMigrated,
                RateIncrease: false,
                SowStartDate: new DateOnly(2024, 12, 31),
                SowEndDate: new DateOnly(2024, 1, 1),
                Note: null
            ),
            isMigrationPrincipal: true,
            TestContext.Current.CancellationToken
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
    }

    [Fact]
    public async Task ACompassSuperAdmin_MayNotLoadABackwardsDateRange_UnderAnyType()
    {
        // Arrange — the corollary that matters: the date exemption travels with the TYPE, and the
        // type is now unreachable to a human. So no application caller can write a backwards range.
        var service = CreateService(Guid.NewGuid());

        // Act
        var result = await service.CreateAsync(
            new CompassSowRequest(
                ClientAssignmentId: 1,
                SowType: SowType.InitialContract,
                RateIncrease: false,
                SowStartDate: new DateOnly(2024, 12, 31),
                SowEndDate: new DateOnly(2024, 1, 1),
                Note: null
            ),
            isMigrationPrincipal: false,
            TestContext.Current.CancellationToken
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
    }
}
