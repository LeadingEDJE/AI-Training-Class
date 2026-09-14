using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Covers the bound on <c>notification_logs.error_message</c> and the truncation that keeps writers inside
/// it.
/// </summary>
/// <remarks>
/// The bound is load-bearing rather than cosmetic: a longer value raises PostgreSQL <c>22001</c> when the
/// row saves, and in <c>NotificationRetryJob</c> that aborts the pass with <c>retry_count</c> still 0, so
/// the notice re-sends on every pass — #591's duplicate storm re-entered through the column that records
/// the failure.
/// </remarks>
public class NotificationErrorMessageTests
{
    [Fact]
    public void Truncate_Null_ReturnsNull()
    {
        // Arrange, Act
        var result = NotificationErrorMessage.Truncate(null);

        // Assert -- a Sent row legitimately clears the column
        result.ShouldBeNull();
    }

    [Fact]
    public void Truncate_AMessageThatFits_ReturnsItUnchanged()
    {
        // Arrange
        var message = new string('a', NotificationErrorMessage.MaxLength);

        // Act
        var result = NotificationErrorMessage.Truncate(message);

        // Assert -- exactly at the bound is inside it; truncating here would lose a character for nothing
        result.ShouldBe(message);
    }

    [Fact]
    public void Truncate_AnOverLongMessage_FitsTheColumnAndSaysItWasCut()
    {
        // Arrange
        var message = new string('a', NotificationErrorMessage.MaxLength + 1);

        // Act
        var result = NotificationErrorMessage.Truncate(message);

        // Assert
        result.ShouldNotBeNull();
        result.Length.ShouldBe(NotificationErrorMessage.MaxLength);
        result.ShouldEndWith(NotificationErrorMessage.TruncationMarker);
        result.ShouldStartWith("aaaa");
    }

    [Fact]
    public void Truncate_ACutLandingInsideASurrogatePair_DoesNotSplitIt()
    {
        // Arrange -- an emoji straddling the cut. A lone surrogate is not valid UTF-8, so Npgsql cannot
        // encode it: splitting one would trade a length error for an encoding error, which is the worse
        // of the two because it does not name the column.
        var cut = NotificationErrorMessage.MaxLength - NotificationErrorMessage.TruncationMarker.Length;
        var message = new string('a', cut - 1) + "\U0001F525" + new string('b', 100);

        // Act
        var result = NotificationErrorMessage.Truncate(message);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldEndWith(NotificationErrorMessage.TruncationMarker);
        result.Length.ShouldBe(NotificationErrorMessage.MaxLength - 1, "the pair is dropped whole");
        char.IsSurrogate(result[cut - 2]).ShouldBeFalse("no half of a surrogate pair may survive the cut");
    }

    [Fact]
    public void TheEfModel_BoundsErrorMessageAtTheSameLengthTheWritersTruncateTo()
    {
        // Arrange -- be exact about what this catches, because the obvious reading is wrong.
        // `NotificationLogConfiguration` READS this constant rather than repeating the number, so
        // changing the constant does NOT fail this test: the model follows it and the two still agree.
        // What it catches is someone replacing that reference with a literal that differs -- the
        // realistic drift, since a literal is what a reader reaches for first.
        //
        // Changing the CONSTANT is caught in two other places, both measured by mutating 2000 -> 4000:
        //   * this suite, but by a different test --
        //     `NotificationRetryJobChannelTests.ARetryThatThrowsAnOverLongReason_TruncatesItToTheColumnBound`
        //     pins 2000 verbatim on purpose, and is the ONLY unit test that fails (1 of 3412);
        //   * the integration suite -- `IntegrationTestFactory` calls `Database.MigrateAsync()`, and EF
        //     Core raises `PendingModelChangesWarning` as an error, so a widened column with no
        //     migration fails all five NotificationRetryJobPersistenceTests before they assert anything.
        // Do not restate either guard here; a unit test cannot see the second one at all.
        using var context = new LeapDbContext(
            new DbContextOptionsBuilder<LeapDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        // Act
        var configured = context.Model
            .FindEntityType(typeof(NotificationLog))!
            .FindProperty(nameof(NotificationLog.ErrorMessage))!
            .GetMaxLength();

        // Assert
        configured.ShouldBe(NotificationErrorMessage.MaxLength);
    }
}
