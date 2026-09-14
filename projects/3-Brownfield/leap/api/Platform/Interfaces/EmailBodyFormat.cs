namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// What a body handed to <see cref="IEmailSender"/> actually is, declared by the caller.
/// </summary>
/// <remarks>
/// Declared rather than sniffed. Two senders used to guess independently and disagree: the SES sender
/// put every body in <c>HtmlBody</c>, so a plain-text name containing <c>&amp;</c> arrived as a broken
/// entity and one containing <c>&lt;</c> swallowed the rest of the line, while the environment stamp
/// matched a bare address in angle brackets as markup and banner-wrapped plain text. A regex cannot
/// recover an intent the caller already knows. Enforced at every call site by
/// <c>EmailBodyFormatCallSiteTests</c>.
/// </remarks>
public enum EmailBodyFormat
{
    /// <summary>An HTML document or fragment. A plain-text alternative is derived from it.</summary>
    Html,

    /// <summary>Plain text. Delivered verbatim, with no markup added and nothing escaped.</summary>
    PlainText,
}
