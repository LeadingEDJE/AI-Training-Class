using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// The translation of a lost SOW overlap/CHECK race into the same rejection value the service's own
/// pre-check returns (research R-4). Mirrors <c>CompassUnitOfWorkTests</c>' style for fabricating a
/// <see cref="PostgresException"/> carrying a specific SQLSTATE.
/// </summary>
public class CompassWriteFailureTests
{
    private static DbUpdateException FailureWithSqlState(string sqlState) =>
        new(
            "An error occurred while saving the entity changes.",
            new PostgresException(
                messageText: "simulated violation",
                severity: "ERROR",
                invariantSeverity: "ERROR",
                sqlState: sqlState));

    [Fact]
    public void IsTranslatable_OnTheExclusionViolation_ReturnsTrue()
    {
        // Arrange — the SOW non-overlap constraint, ex_sow_no_overlap_per_assignment.
        var exception = FailureWithSqlState(CompassWriteFailure.ExclusionViolationSqlState);

        // Act & Assert
        CompassWriteFailure.IsTranslatable(exception).ShouldBeTrue();
    }

    [Fact]
    public void MessageFor_OnTheExclusionViolation_NamesTheOverlap()
    {
        // Arrange
        var exception = FailureWithSqlState(CompassWriteFailure.ExclusionViolationSqlState);

        // Act
        var message = CompassWriteFailure.MessageFor(exception);

        // Assert — FR-019 requires a message naming the problem, not a raw storage error.
        message.ShouldNotBeNullOrWhiteSpace();
        message.ShouldContain("overlap");
    }

    [Fact]
    public void IsTranslatable_OnACheckViolation_ReturnsTrue()
    {
        // Arrange — any of the three SOW CHECKs, or the assignment end-on-or-after-start CHECK.
        var exception = FailureWithSqlState(CompassWriteFailure.CheckViolationSqlState);

        // Act & Assert
        CompassWriteFailure.IsTranslatable(exception).ShouldBeTrue();
    }

    [Fact]
    public void MessageFor_OnACheckViolation_NamesTheDateOrderProblem()
    {
        // Arrange
        var exception = FailureWithSqlState(CompassWriteFailure.CheckViolationSqlState);

        // Act
        var message = CompassWriteFailure.MessageFor(exception);

        // Assert — FR-020/FR-023.
        message.ShouldNotBeNullOrWhiteSpace();
        message.ShouldContain("start date");
    }

    [Fact]
    public void IsTranslatable_OnAnUntranslatedSqlState_ReturnsFalse()
    {
        // Arrange — a real SQLSTATE this translator does not own, e.g. a deadlock.
        var exception = FailureWithSqlState("40P01");

        // Act & Assert
        CompassWriteFailure.IsTranslatable(exception).ShouldBeFalse(
            "translating every failure into an overlap/date-order message would tell a caller to fix "
                + "something that was never the problem");
    }

    [Fact]
    public void MessageFor_OnAnUntranslatedSqlState_Throws()
    {
        // Arrange
        var exception = FailureWithSqlState("40P01");

        // Act & Assert
        Should.Throw<ArgumentException>(() => CompassWriteFailure.MessageFor(exception));
    }

    [Fact]
    public void IsTranslatable_OnANonPostgresInnerException_ReturnsFalse()
    {
        // Arrange — a save can fail for reasons that have nothing to do with a Postgres constraint.
        var exception = new DbUpdateException("the connection dropped", new InvalidOperationException());

        // Act & Assert
        CompassWriteFailure.IsTranslatable(exception).ShouldBeFalse();
    }

    [Fact]
    public void AnUntranslatedFailure_PropagatesThroughTheExceptionFilterUnchanged()
    {
        // Arrange — the intended call-site shape (this file's own doc comment), proven directly: an
        // untranslated SQLSTATE must make the `when` clause fail so the exception rethrows, exactly as
        // CompassUnitOfWork.IsUniqueViolation already does for the unique-key case.
        var exception = FailureWithSqlState("40P01");

        // Act & Assert
        var thrown = Should.Throw<DbUpdateException>(() =>
        {
            try
            {
                throw exception;
            }
            catch (DbUpdateException ex) when (CompassWriteFailure.IsTranslatable(ex))
            {
                throw new InvalidOperationException("should never be reached — the filter must fail");
            }
        });

        thrown.ShouldBeSameAs(exception);
    }
}
