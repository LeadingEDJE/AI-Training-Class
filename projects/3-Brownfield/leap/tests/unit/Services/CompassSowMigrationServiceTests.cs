using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Creating a contract period (SOW).
/// </summary>
/// <remarks>
/// <para>
/// Two of the three database rules on <c>compass.sow</c> are partial — they exempt
/// <c>LegacyMigrated</c> — because a faithful TPS load must be admitted exactly as the legacy system
/// recorded it, backwards ranges and overlaps included. Everything Compass itself creates stays fully
/// protected.
/// </para>
/// <para>
/// The exemption's blast radius is the subject of
/// <c>CompassSowLegacyMigratedRestrictionTests</c>; this file covers the ordinary rules.
/// </para>
/// </remarks>
public class CompassSowMigrationServiceTests
{
    private readonly ICompassSowRepository _repository = Substitute.For<ICompassSowRepository>();
    private readonly ICompassUnitOfWork _unitOfWork = Substitute.For<ICompassUnitOfWork>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly ICurrentUserContext _currentUser = Substitute.For<ICurrentUserContext>();

    private CompassSowMigrationService CreateService(Guid? actor = null)
    {
        _currentUser.EdjeId.Returns(actor ?? Guid.NewGuid());
        _repository
            .AssignmentExistsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return new CompassSowMigrationService(_repository, _unitOfWork, _audit, _currentUser);
    }

    private static CompassSowRequest Request(
        SowType type = SowType.InitialContract,
        DateOnly? start = null,
        DateOnly? end = null,
        bool rateIncrease = false
    ) =>
        new(
            ClientAssignmentId: 1,
            SowType: type,
            RateIncrease: rateIncrease,
            SowStartDate: start ?? new DateOnly(2024, 1, 1),
            SowEndDate: end ?? new DateOnly(2024, 12, 31),
            Note: null
        );

    [Fact]
    public async Task CreateAsync_WhenTheAssignmentDoesNotExist_IsNotFound()
    {
        // Arrange
        var service = CreateService();
        _repository
            .AssignmentExistsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await service.CreateAsync(
            Request(),
            isMigrationPrincipal: false,
            TestContext.Current.CancellationToken
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.NotFound);
    }

    [Fact]
    public async Task CreateAsync_WithEndDateBeforeStartDate_IsRejected_ForANonLegacyType()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.CreateAsync(
            Request(start: new DateOnly(2024, 6, 1), end: new DateOnly(2024, 5, 1)),
            isMigrationPrincipal: false,
            TestContext.Current.CancellationToken
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
    }

    [Fact]
    public async Task CreateAsync_WithRateIncrease_IsRejected_OnAnInitialContract()
    {
        // Arrange — a database CHECK restricts rate increases to extensions. Catching it here gives a
        // 400 naming the field instead of a 500 naming a constraint.
        var service = CreateService();

        // Act
        var result = await service.CreateAsync(
            Request(type: SowType.InitialContract, rateIncrease: true),
            isMigrationPrincipal: false,
            TestContext.Current.CancellationToken
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
        result.Error!.ShouldContain("rate increase");
    }

    [Fact]
    public async Task CreateAsync_WithRateIncrease_IsRejected_OnALegacyMigratedPeriod()
    {
        // Arrange — LegacyMigrated does NOT inherit the extension's permission. TPS tracks no
        // extensions at all, so a migrated row asserting a rate increase is meaningless.
        var service = CreateService();

        // Act
        var result = await service.CreateAsync(
            Request(type: SowType.LegacyMigrated, rateIncrease: true),
            isMigrationPrincipal: true,
            TestContext.Current.CancellationToken
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.ValidationError);
    }

    [Fact]
    public async Task CreateAsync_WithRateIncrease_IsAccepted_OnAnExtension()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.CreateAsync(
            Request(type: SowType.SowExtension, rateIncrease: true),
            isMigrationPrincipal: false,
            TestContext.Current.CancellationToken
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
    }

    [Fact]
    public async Task CreateAsync_ForANonLegacyType_MarksTheRowAsHavingPassedValidation()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.CreateAsync(
            Request(),
            isMigrationPrincipal: false,
            TestContext.Current.CancellationToken
        );

        // Assert — it satisfied both rules in full on the way in, so the flag is honest.
        result.Status.ShouldBe(CompassWriteStatus.Success);
        result.Value!.HasPassedApplicationValidation.ShouldBeTrue();
    }

    /// <summary>
    /// Fabricates the <see cref="DbUpdateException"/> a lost constraint race arrives as. Mirrors the
    /// helper in <c>CompassWriteFailureTests</c>.
    /// </summary>
    private static DbUpdateException FailureWithSqlState(string sqlState) =>
        new(
            "An error occurred while saving the entity changes.",
            new PostgresException(
                messageText: "simulated violation",
                severity: "ERROR",
                invariantSeverity: "ERROR",
                sqlState: sqlState));

    /// <summary>
    /// A create that clears the overlap pre-check and then loses the constraint race answers the SAME
    /// conflict the pre-check would have, rather than escaping as an unhandled failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pre-check is a check-then-act: two concurrent creates can both clear it and only one can
    /// win <c>ex_sow_no_overlap_per_assignment</c>. Both translatable SQLSTATEs are exercised because
    /// the catch filter admits both, and a filter narrowed to one of them would still pass a test that
    /// only ever threw the other.
    /// </para>
    /// <para>
    /// This is not spec 006 T024. That task asks for the same property at the HTTP boundary
    /// through <c>CompassSowEndpoints</c> — a different surface from this service. This covers the
    /// migration service's own catch and does not discharge T024.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(CompassWriteFailure.ExclusionViolationSqlState)]
    [InlineData(CompassWriteFailure.CheckViolationSqlState)]
    public async Task CreateAsync_LosingTheConstraintRace_IsAConflictAndNotAnUnhandledFailure(
        string sqlState
    )
    {
        // Arrange — the pre-check passes, so the write reaches the save and the save is what fails.
        var service = CreateService();
        _unitOfWork
            .When(unitOfWork => unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw FailureWithSqlState(sqlState));

        // Act
        var result = await service.CreateAsync(
            Request(),
            isMigrationPrincipal: false,
            TestContext.Current.CancellationToken
        );

        // Assert — the TRANSLATED message, not a generic one: a caller who lost the race should read
        // the same explanation as one the pre-check refused.
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        result.Error.ShouldBe(CompassWriteFailure.MessageFor(FailureWithSqlState(sqlState)));
    }

    [Fact]
    public async Task CreateAsync_OnSuccess_SavesOnceAndAuditsTheWrite()
    {
        // Arrange
        var service = CreateService();

        // Act
        await service.CreateAsync(
            Request(),
            isMigrationPrincipal: false,
            TestContext.Current.CancellationToken
        );

        // Assert — Principle VIII names SOWs in the attributable-writes list.
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(Arg.Any<AuditEntry>());
    }

    /// <summary>
    /// The audit entity type is <c>CompassSow</c> — the same string the Ops surface writes.
    /// </summary>
    /// <remarks>
    /// This is a cross-service invariant, and it is the assertion that stops the two drifting
    /// apart again. <see cref="CompassSowService"/> and
    /// <see cref="CompassSowMigrationService"/> write to the same <c>compass.sow</c> table, so a
    /// query for "every contract-period write" must find both under ONE entity type or it silently
    /// reports half the history. They HAD diverged — the Ops surface wrote <c>"Sow"</c> while this
    /// one wrote <c>"CompassSow"</c> — and nothing failed, because each side was internally
    /// consistent and only this side was unasserted. Its counterpart lives in
    /// <c>CompassSowServiceTests</c>; changing one string without the other now turns a suite red
    /// instead of quietly splitting the audit trail.
    /// </remarks>
    [Fact]
    public async Task CreateAsync_AuditsUnderTheSameEntityTypeAsTheOpsSurface()
    {
        // Arrange
        var service = CreateService();

        // Act
        await service.CreateAsync(
            Request(),
            isMigrationPrincipal: false,
            TestContext.Current.CancellationToken
        );

        // Assert
        await _audit
            .Received(1)
            .LogAsync(Arg.Is<AuditEntry>(e => e.EntityType == "CompassSow"));
    }

    [Fact]
    public async Task CreateAsync_ByTheMigrationPrincipal_StampsTheMigrationAuditTrigger()
    {
        // Arrange
        var service = CreateService(actor: MigrationPrincipal.EdjeId);

        // Act
        await service.CreateAsync(
            Request(type: SowType.LegacyMigrated),
            isMigrationPrincipal: true,
            TestContext.Current.CancellationToken
        );

        // Assert — Principle VIII: distinguishable from operator activity.
        await _audit
            .Received(1)
            .LogAsync(Arg.Is<AuditEntry>(e => e.TriggeredBy == MigrationPrincipal.AuditTriggeredBy));
    }

    [Fact]
    public async Task GetAsync_ReturnsTheSowTheRepositoryFinds()
    {
        // Arrange — a pass-through, but an UNTESTED pass-through is still a route that has never
        // been shown to reach its repository. The per-file gate requires 100% on changed files
        // precisely so a read added beside a write does not slip through uncovered.
        var expected = new CompassSowDto(
            Id: 7,
            ClientAssignmentId: 42,
            SowType: SowType.InitialContract,
            RateIncrease: false,
            HasPassedApplicationValidation: true,
            SowStartDate: new DateOnly(2024, 1, 1),
            SowEndDate: new DateOnly(2024, 12, 31),
            Note: null
        );

        _repository.GetAsync(7, Arg.Any<CancellationToken>()).Returns(expected);

        // Act
        var actual = await CreateService().GetAsync(7, TestContext.Current.CancellationToken);

        // Assert
        actual.ShouldBe(expected);
    }

    // ---------------------------------------------------------------- the overlap refusal (#306)

    [Fact]
    public async Task CreateAsync_WhenThePeriodOverlaps_NamesTheClashingPeriodAsMonthDayYear()
    {
        // Arrange -- FR-023 wants a message the caller can ACT on, so the clashing period is named
        // rather than merely reported as a conflict. Issue #234 governs how a date a person reads is
        // rendered, and this message is read by whoever ran the migration: mm/dd/yyyy, not the ISO
        // form the wire uses. This surface arrived with #281 carrying the ISO form and could not fix
        // it -- CompassDisplayDate lands with this feature.
        var service = CreateService();
        _repository
            .GetOverlappingAsync(
                Arg.Any<int>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<IReadOnlyList<Sow>>(
                [
                    new Sow
                    {
                        Id = 42,
                        ClientAssignmentId = 1,
                        SowType = SowType.InitialContract,
                        SowStartDate = new DateOnly(2024, 4, 1),
                        SowEndDate = new DateOnly(2024, 6, 30),
                        HasPassedApplicationValidation = true,
                    },
                ]
            );

        // Act
        var result = await service.CreateAsync(
            Request(start: new DateOnly(2024, 5, 1), end: new DateOnly(2024, 7, 31)),
            isMigrationPrincipal: false,
            TestContext.Current.CancellationToken
        );

        // Assert -- the whole sentence, so a later edit cannot quietly drop the dates back to ISO.
        result.Status.ShouldBe(CompassWriteStatus.Conflict);
        result.Error.ShouldBe(
            "This period overlaps an existing one (04/01/2024 to 06/30/2024) on the same assignment."
        );
    }

    [Fact]
    public async Task CreateAsync_ForALegacyMigratedPeriod_DoesNotEvenCheckForAnOverlap()
    {
        // Arrange -- the exemption is the point of the migration surface: a faithful TPS load admits
        // what the legacy system recorded, overlaps included. The pre-check exists to NAME a clash,
        // so running it for a type the constraint exempts would refuse exactly the rows this path
        // exists to accept.
        var service = CreateService();

        // Act
        var result = await service.CreateAsync(
            Request(type: SowType.LegacyMigrated),
            isMigrationPrincipal: true,
            TestContext.Current.CancellationToken
        );

        // Assert
        result.Status.ShouldBe(CompassWriteStatus.Success);
        await _repository
            .DidNotReceive()
            .GetOverlappingAsync(
                Arg.Any<int>(),
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenTheSowDoesNotExist()
    {
        // Arrange — the null travels back so the endpoint can answer 404 rather than 200-with-null.
        _repository.GetAsync(404, Arg.Any<CancellationToken>()).Returns((CompassSowDto?)null);

        // Act
        var actual = await CreateService().GetAsync(404, TestContext.Current.CancellationToken);

        // Assert
        actual.ShouldBeNull();
    }
}
