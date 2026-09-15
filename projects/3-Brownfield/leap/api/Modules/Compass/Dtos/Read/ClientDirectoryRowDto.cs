namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>One row of the Client Directory (AC-12).</summary>
public sealed class ClientDirectoryRowDto
{
    /// <summary>The client's identifier, carrying AC-12's link into the client view.</summary>
    public int Id { get; init; }

    /// <summary>The client's name — the column AC-12's search matches against.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>
    /// The derived status: exactly <c>Active</c> or <c>Inactive</c> (AC-42, BR-11).
    /// </summary>
    /// <remarks>
    /// Can be null for a client with no assignments yet, rendered as a blank cell rather than
    /// <c>Inactive</c>. Stored alongside the client record and refreshed on a nightly job.
    /// </remarks>
    public string Status { get; init; } = string.Empty;
}
