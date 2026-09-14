using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Auth;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// How a Compass write records WHO triggered it.
/// </summary>
/// <remarks>
/// <para>
/// Constitution Principle VIII: "Bulk and migration writes MUST be attributed to a named system
/// principal, distinguishable from operator activity." Attribution that cannot tell a
/// migration from an administrator satisfies the letter and not the point — the whole reason the
/// trail exists is to answer "was this a person?" months later.
/// </para>
/// <para>
/// The distinction is drawn on the actor's identifier rather than on a role, because the migration
/// principal holds the Compass root role and so is indistinguishable from a Super Admin by
/// authority alone.
/// </para>
/// </remarks>
public class CompassAuditActorTests
{
    [Fact]
    public void For_TheMigrationPrincipal_RecordsTheMigrationTrigger()
    {
        // Act
        var trigger = CompassAuditTrigger.For(MigrationPrincipal.EdjeId);

        // Assert
        trigger.ShouldBe(MigrationPrincipal.AuditTriggeredBy);
    }

    [Fact]
    public void For_AHumanAdministrator_RecordsTheOperatorTrigger()
    {
        // Act
        var trigger = CompassAuditTrigger.For(Guid.NewGuid());

        // Assert
        trigger.ShouldBe(CompassAuditTrigger.Operator);
    }

    [Fact]
    public void TheTwoTriggers_AreDistinguishable()
    {
        // Assert — the assertion Principle VIII actually cares about. If these ever collapse to the
        // same string, every migration write becomes indistinguishable from an administrator's.
        CompassAuditTrigger.Operator.ShouldNotBe(MigrationPrincipal.AuditTriggeredBy);
    }

    [Fact]
    public void Operator_IsTheValueTheModuleAlreadyUsed()
    {
        // Assert — pins the pre-existing wire value. Changing it would silently reclassify every
        // historical Compass audit row when a reader groups by TriggeredBy.
        CompassAuditTrigger.Operator.ShouldBe("Compass Admin");
    }

    [Fact]
    public void For_TheEmptyGuid_IsTreatedAsAnOperator_NotAsTheMigration()
    {
        // Act — a defaulted/unset actor must never be mistaken for the migration principal, which
        // would attribute a real person's write to a system identity.
        var trigger = CompassAuditTrigger.For(Guid.Empty);

        // Assert
        trigger.ShouldBe(CompassAuditTrigger.Operator);
    }
}
