namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>A client's derived status — three-valued and total.</summary>
/// <remarks>
/// Three-valued and total: every client resolves to exactly one member. The value space is closed —
/// no <c>Unknown</c>, no <c>None</c>, never null in any DTO — which is what lets the Client Directory
/// sort the column.
///
/// Never stored: a stored value can disagree with the assignments it summarises, and
/// <c>CompassSchemaFromErdTests.Client_HasNoStoredStatusColumn</c> fails the build if a column
/// appears. The ordinals therefore carry no meaning. Not the same type as
/// <see cref="AssignmentStatus"/>: an assignment cannot be <see cref="Former"/>, and that enum's
/// remarks say why the two stay separate.
/// </remarks>
public enum ClientStatus
{
    /// <summary>The client holds at least one current assignment.</summary>
    Active,

    /// <summary>
    /// No assignment has ever existed for this client — the state of every newly created one.
    /// Inactive clients stay fully selectable and editable; status gates nothing.
    /// </summary>
    Inactive,

    /// <summary>
    /// We worked with this client and no longer do: assignments exist, but none is current.
    /// Re-engaging needs no action beyond creating an assignment — the status follows.
    /// </summary>
    Former,
}
