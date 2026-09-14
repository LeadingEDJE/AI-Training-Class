using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// The registry's own behaviour: dispatch, the allow-by-default decision of 2026-09-04, the
/// duplicate-registration failure, and the <c>HasRule</c> probe that keeps an unregistered entity
/// type from paying for a directory read.
/// </summary>
public class AuditTrailAccessPolicyTests
{
    private static AuditTrailAccessRequest Request => new("42", "emp-001");

    [Fact]
    public async Task EvaluateAsync_RegisteredEntityType_DelegatesToTheRule()
    {
        // Arrange
        var rule = new StubRule("Timesheet", AuditTrailAccessOutcome.Refused);
        var policy = new AuditTrailAccessPolicy([rule]);

        // Act
        var outcome = await policy.EvaluateAsync("Timesheet", Request);

        // Assert
        outcome.Result.ShouldBe(AuditTrailAccess.Refused);
        rule.ReceivedRequest.ShouldBe(Request);
    }

    [Fact]
    public async Task EvaluateAsync_RuleReturnsInvalid_RelaysTheMessageUnmodified()
    {
        // Arrange — the message is module knowledge; Platform must not compose or wrap it
        const string message = "Invalid entityId for Timesheet.";
        var policy = new AuditTrailAccessPolicy(
            [new StubRule("Timesheet", AuditTrailAccessOutcome.Invalid(message))]);

        // Act
        var outcome = await policy.EvaluateAsync("Timesheet", Request);

        // Assert
        outcome.Result.ShouldBe(AuditTrailAccess.Invalid);
        outcome.Message.ShouldBe(message);
    }

    [Fact]
    public async Task EvaluateAsync_UnregisteredEntityType_ReturnsPermitted()
    {
        // Arrange — the owner decision of 2026-09-04: an unregistered type is ALLOWED. This is the
        // registry's central semantic, and #557 is what deliberately inverts it.
        var policy = new AuditTrailAccessPolicy(
            [new StubRule("Timesheet", AuditTrailAccessOutcome.Refused)]);

        // Act
        var outcome = await policy.EvaluateAsync("UserRole", Request);

        // Assert
        outcome.Result.ShouldBe(AuditTrailAccess.Permitted);
        outcome.Message.ShouldBeNull();
    }

    [Fact]
    public async Task EvaluateAsync_EntityTypeInADifferentCase_StillFindsTheRule()
    {
        // Arrange — preserves the pre-#528 endpoint's OrdinalIgnoreCase comparison. Required by
        // FR-010, not by security: without it a lowercasing caller falls to allow-by-default, which
        // turns a 403 into a 200.
        var policy = new AuditTrailAccessPolicy(
            [new StubRule("Timesheet", AuditTrailAccessOutcome.Refused)]);

        // Act
        var outcome = await policy.EvaluateAsync("timesheet", Request);

        // Assert
        outcome.Result.ShouldBe(AuditTrailAccess.Refused);
    }

    [Fact]
    public void Constructor_TwoRulesForTheSameEntityType_ThrowsNamingTheEntityType()
    {
        // Arrange — a duplicate is a programming error, not a precedence question
        IAuditTrailAccessRule[] rules =
        [
            new StubRule("Timesheet", AuditTrailAccessOutcome.Refused),
            new StubRule("timesheet", AuditTrailAccessOutcome.Permitted),
        ];

        // Act
        var thrown = Should.Throw<InvalidOperationException>(() => new AuditTrailAccessPolicy(rules));

        // Assert — the message must name the offender, or the failure is unactionable in CI
        thrown.Message.ShouldContain("Timesheet");
    }

    [Fact]
    public void HasRule_RegisteredEntityType_IsTrue_AndIsCaseInsensitive()
    {
        // Arrange
        var policy = new AuditTrailAccessPolicy(
            [new StubRule("Timesheet", AuditTrailAccessOutcome.Refused)]);

        // Act & Assert
        policy.HasRule("Timesheet").ShouldBeTrue();
        policy.HasRule("timesheet").ShouldBeTrue();
    }

    [Fact]
    public async Task HasRule_UnregisteredEntityType_IsFalse_AndAgreesWithEvaluateAsync()
    {
        // Arrange — HasRule(t) == false MUST imply EvaluateAsync(t, ...) would permit, or the
        // endpoint's short-circuit changes behaviour instead of just avoiding a directory read.
        var policy = new AuditTrailAccessPolicy(
            [new StubRule("Timesheet", AuditTrailAccessOutcome.Refused)]);

        // Act
        var hasRule = policy.HasRule("EmployeeYearBalance");
        var outcome = await policy.EvaluateAsync("EmployeeYearBalance", Request);

        // Assert
        hasRule.ShouldBeFalse();
        outcome.Result.ShouldBe(AuditTrailAccess.Permitted);
    }

    [Fact]
    public void HasRule_NoRulesRegisteredAtAll_IsFalse()
    {
        // Arrange — the empty registry is reachable: a host that removes the Timesheet module's
        // registration resolves an empty enumerable rather than failing.
        var policy = new AuditTrailAccessPolicy([]);

        // Act & Assert
        policy.HasRule("Timesheet").ShouldBeFalse();
    }

    private sealed class StubRule(string entityType, AuditTrailAccessOutcome outcome)
        : IAuditTrailAccessRule
    {
        public string EntityType => entityType;

        public AuditTrailAccessRequest? ReceivedRequest { get; private set; }

        public Task<AuditTrailAccessOutcome> EvaluateAsync(AuditTrailAccessRequest request)
        {
            ReceivedRequest = request;
            return Task.FromResult(outcome);
        }
    }
}
