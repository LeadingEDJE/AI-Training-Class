using LeadingEDJE.Leap.Api.Platform.Services.Logging;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services.Logging;

public class LogSanitizerTests
{
    [Fact]
    public void Clean_NullInput_ReturnsEmpty()
    {
        // Act
        var result = LogSanitizer.Clean(null);

        // Assert
        result.ShouldBe(string.Empty);
    }

    [Fact]
    public void Clean_EmptyInput_ReturnsEmpty()
    {
        // Act
        var result = LogSanitizer.Clean(string.Empty);

        // Assert
        result.ShouldBe(string.Empty);
    }

    [Fact]
    public void Clean_SafeAsciiInput_ReturnsUnchanged()
    {
        // Act
        var result = LogSanitizer.Clean("hello world");

        // Assert
        result.ShouldBe("hello world");
    }

    [Fact]
    public void Clean_WithNewline_StripsNewline()
    {
        // Act
        var result = LogSanitizer.Clean("line1\nline2");

        // Assert
        result.ShouldBe("line1line2");
    }

    [Fact]
    public void Clean_WithCarriageReturnLineFeed_StripsBoth()
    {
        // Act
        var result = LogSanitizer.Clean("line1\r\nline2");

        // Assert
        result.ShouldBe("line1line2");
    }

    [Fact]
    public void Clean_WithTabControl_StripsControlChar()
    {
        // Act
        var result = LogSanitizer.Clean("tab\there");

        // Assert
        result.ShouldBe("tabhere");
    }

    [Fact]
    public void Clean_WithDelCharacter_StripsDel()
    {
        // Act
        var result = LogSanitizer.Clean("before\u007Fafter");

        // Assert
        result.ShouldBe("beforeafter");
    }

    [Fact]
    public void Clean_WithPrintableUnicode_Preserves()
    {
        // Act
        var result = LogSanitizer.Clean("café résumé");

        // Assert
        result.ShouldBe("café résumé");
    }

    [Fact]
    public void Clean_WithLongInput_TruncatesWithEllipsis()
    {
        // Arrange
        var input = new string('a', 300);

        // Act
        var result = LogSanitizer.Clean(input);

        // Assert — 256 chars + ellipsis character
        result.Length.ShouldBe(257);
        result.ShouldEndWith("…");
    }

    // ---------------------------------------------------------------- RedactEmail (CodeQL #72, #66, #91)

    [Fact]
    public void RedactEmail_NullInput_ReturnsEmpty()
    {
        // Act
        var result = LogSanitizer.RedactEmail(null);

        // Assert
        result.ShouldBe(string.Empty);
    }

    [Fact]
    public void RedactEmail_EmptyInput_ReturnsEmpty()
    {
        // Act
        var result = LogSanitizer.RedactEmail(string.Empty);

        // Assert
        result.ShouldBe(string.Empty);
    }

    [Fact]
    public void RedactEmail_RemovesTheLocalPart()
    {
        // Act
        var result = LogSanitizer.RedactEmail("jane.doe@leadingedje.com");

        // Assert -- the identifying half must not survive into the log.
        result.ShouldNotContain("jane");
        result.ShouldNotContain("doe");
    }

    [Fact]
    public void RedactEmail_KeepsTheDomain()
    {
        // Act
        var result = LogSanitizer.RedactEmail("jane.doe@leadingedje.com");

        // Assert -- the domain is what tells an operator whether a notice went to a colleague or
        // somewhere it should not have, and it identifies nobody on its own.
        result.ShouldContain("@leadingedje.com");
    }

    [Fact]
    public void RedactEmail_IsStableForTheSameAddress()
    {
        // Act
        var first = LogSanitizer.RedactEmail("jane.doe@leadingedje.com");
        var second = LogSanitizer.RedactEmail("jane.doe@leadingedje.com");

        // Assert -- stability is the whole point: without it two lines about one recipient cannot be
        // tied together, and the log stops answering the question it exists for.
        first.ShouldBe(second);
    }

    [Fact]
    public void RedactEmail_DistinguishesTwoRecipientsAtTheSameDomain()
    {
        // Act
        var jane = LogSanitizer.RedactEmail("jane.doe@leadingedje.com");
        var john = LogSanitizer.RedactEmail("john.roe@leadingedje.com");

        // Assert -- almost every recipient here is @leadingedje.com, so masking the local part with
        // no discriminator would render every line identical and the log useless.
        jane.ShouldNotBe(john);
    }

    [Fact]
    public void RedactEmail_TreatsCaseAsTheSameRecipient()
    {
        // Act
        var lower = LogSanitizer.RedactEmail("jane.doe@leadingedje.com");
        var mixed = LogSanitizer.RedactEmail("Jane.Doe@LeadingEDJE.com");

        // Assert -- the same person signing in with different capitalisation must correlate.
        lower.ShouldBe(mixed);
    }

    [Fact]
    public void RedactEmail_WithNoAtSign_MasksTheWholeValue()
    {
        // Act
        var result = LogSanitizer.RedactEmail("not-an-email-at-all");

        // Assert -- a malformed value is still untrusted input and must not be echoed.
        result.ShouldNotContain("not-an-email-at-all");
        result.ShouldNotBeEmpty();
    }

    [Fact]
    public void RedactEmail_UsesTheLastAtSignAsTheSeparator()
    {
        // Act -- a quoted local part may itself contain '@'.
        var result = LogSanitizer.RedactEmail("\"odd@local\"@leadingedje.com");

        // Assert
        result.ShouldContain("@leadingedje.com");
        result.ShouldNotContain("odd");
    }

    [Fact]
    public void RedactEmail_StripsNewlinesFromTheDomain()
    {
        // Act -- redaction does not replace log-forging protection, it has to include it.
        var result = LogSanitizer.RedactEmail("a@evil.test\nFATAL fake log line");

        // Assert -- the guarantee Clean actually makes, and the one CodeQL recognises, is that no
        // NEW log LINE can be forged. The injected text still appears, collapsed onto this line; it
        // cannot masquerade as a separate entry. Asserting its absence would be asserting a
        // guarantee this repository has never made.
        result.ShouldNotContain("\n");
        result.ShouldNotContain("\r");
    }
    [Fact]
    public void RedactEmail_PseudonymIsNotAnUnsaltedDigestOfTheAddress()
    {
        // Arrange -- the review finding on PR #371, reproduced: an unsalted SHA-256 truncated to
        // eight hex characters is recoverable by dictionary attack against a small, guessable
        // corpus. Measured before this test was written: 805 `first.last@leadingedje.com`-shaped
        // candidates, sub-millisecond, address recovered. So the pseudonym must not be derivable
        // from the address alone.
        const string Address = "jane.doe@leadingedje.com";
        var unsalted = System.Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(Address)))[..8]
            .ToLowerInvariant();

        // Act
        var result = LogSanitizer.RedactEmail(Address);

        // Assert
        result.Contains(unsalted, StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
            "the pseudonym must not be a plain digest of the address -- an attacker holding the logs "
                + "can enumerate the corpus and reverse it in under a millisecond");
    }
}
