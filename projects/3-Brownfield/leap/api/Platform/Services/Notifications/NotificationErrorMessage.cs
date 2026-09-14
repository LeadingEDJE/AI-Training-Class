namespace LeadingEDJE.Leap.Api.Platform.Services.Notifications;

/// <summary>
/// Owns the persisted bound on <see cref="Domain.NotificationLog.ErrorMessage"/> and the truncation that
/// keeps a reason inside it.
/// </summary>
/// <remarks>
/// <para>
/// The column is <c>varchar(2000)</c>. Anything longer makes PostgreSQL raise <c>22001</c> when the row is
/// saved — which, in the retry job, aborts the pass with <c>retry_count</c> still 0, so the notice re-sends
/// on every pass. That is the duplicate storm #591 exists to stop, re-entered through the error column.
/// </para>
/// <para>
/// The bound lives here rather than as a literal in <c>NotificationLogConfiguration</c> so the mapping and
/// every writer read the same number; <c>NotificationErrorMessageTests</c> fails if the EF model and
/// this constant drift apart.
/// </para>
/// <para>
/// Truncating is not lossy in practice: the full exception already reaches the structured log through
/// <c>LogError(ex, …)</c>. The column is a troubleshooting summary, not the record of the failure.
/// </para>
/// </remarks>
public static class NotificationErrorMessage
{
    /// <summary>The persisted maximum length of the error column, in characters.</summary>
    public const int MaxLength = 2000;

    /// <summary>
    /// Appended to a message that had to be cut, so a reader is never handed a half-sentence that looks
    /// complete. Asserted verbatim by the tests: changing it changes what an operator reads.
    /// </summary>
    public const string TruncationMarker = "… [truncated]";

    /// <summary>
    /// Returns <paramref name="message"/> unchanged when it fits the column, and otherwise the longest
    /// prefix that leaves room for <see cref="TruncationMarker"/>.
    /// </summary>
    /// <remarks>
    /// The cut never lands between the halves of a surrogate pair. A lone surrogate is not valid UTF-8, so
    /// Npgsql cannot encode it — splitting an emoji or a supplementary-plane character would turn a
    /// length problem into an encoding problem, which is a worse failure than the one being fixed.
    /// </remarks>
    public static string? Truncate(string? message)
    {
        if (message is null || message.Length <= MaxLength)
        {
            return message;
        }

        var cut = MaxLength - TruncationMarker.Length;
        if (char.IsHighSurrogate(message[cut - 1]))
        {
            cut--;
        }

        return string.Concat(message.AsSpan(0, cut), TruncationMarker);
    }
}
