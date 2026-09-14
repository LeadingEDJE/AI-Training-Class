using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Tests the system-generated audit reasons Compass configuration writes supply.
/// </summary>
/// <remarks>
/// <para>
/// Why this type exists at all. <c>AuditService.LogAsync</c> throws <c>ArgumentException</c>
/// when an entry's <c>Reason</c> is null or whitespace (research F-1), and none of AC-17, AC-21 or
/// AC-23 asks an administrator to type one. So Compass generates the reason instead. **Do not add a
/// reason field to any configuration form** — that would invent a requirement the PRD does not carry.
/// </para>
/// <para>
/// The blank-subject guard is deliberate: it moves the failure from inside the audit service, where
/// the message names the audit plumbing, to the call site, where it names the caller's mistake.
/// </para>
/// </remarks>
public class CompassAuditReasonTests
{
    [Fact]
    public void Created_ForAnEdjer_ProducesTheReasonTheContractNames()
    {
        // Arrange & Act
        var reason = CompassAuditReason.Created("EDJEr");

        // Assert — the exact wording research F-1 and the API contract use as the worked example.
        reason.ShouldBe("EDJEr created via Compass configuration");
    }

    [Fact]
    public void Updated_ForAClient_ProducesTheMatchingUpdateReason()
    {
        // Arrange & Act
        var reason = CompassAuditReason.Updated("Client");

        // Assert
        reason.ShouldBe("Client updated via Compass configuration");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Created_WithABlankSubject_ThrowsRatherThanProducingAReasonAuditWouldReject(
        string? subject
    )
    {
        // Arrange & Act & Assert — a blank subject would compose a reason that still reads as
        // whitespace-led prose but tells a later reader nothing. Fail at the composition site.
        Should.Throw<ArgumentException>(() => CompassAuditReason.Created(subject!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Updated_WithABlankSubject_Throws(string? subject)
    {
        // Arrange & Act & Assert
        Should.Throw<ArgumentException>(() => CompassAuditReason.Updated(subject!));
    }

    [Fact]
    public void Deleted_ForAnAssignment_ProducesTheMatchingDeleteReason()
    {
        // Arrange & Act — issue #593: the Compass Super Admin-only true delete.
        var reason = CompassAuditReason.Deleted("Assignment");

        // Assert
        reason.ShouldBe("Assignment deleted via Compass configuration");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Deleted_WithABlankSubject_Throws(string? subject)
    {
        // Arrange & Act & Assert
        Should.Throw<ArgumentException>(() => CompassAuditReason.Deleted(subject!));
    }

    [Fact]
    public void EveryReason_IsNonBlank_SoAuditNeverRejectsIt()
    {
        // Arrange & Act & Assert — the invariant the whole type is for, stated once.
        CompassAuditReason.Created("EDJEr").ShouldNotBeNullOrWhiteSpace();
        CompassAuditReason.Updated("EDJEr").ShouldNotBeNullOrWhiteSpace();
        CompassAuditReason.Deleted("EDJEr").ShouldNotBeNullOrWhiteSpace();
    }
}
