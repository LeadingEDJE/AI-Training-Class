namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>A client's derived status — three-valued and total.</summary>
public enum ClientStatus
{
    /// <summary>The client holds at least one current assignment.</summary>
    Active,

    /// <summary>
    /// No assignment has ever existed for this client — the state of every newly created one.
    /// Inactive clients are read-only until an assignment reactivates them; status gates edit
    /// access here.
    /// </summary>
    Inactive,

    /// <summary>
    /// We worked with this client and no longer do: assignments exist, but none is current.
    /// Re-engaging requires manually resetting the status field before a new assignment can be
    /// created.
    /// </summary>
    Former,
}
