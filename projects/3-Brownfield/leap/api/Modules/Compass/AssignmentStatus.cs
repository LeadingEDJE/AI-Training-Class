namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>One assignment's derived currency — binary and total.</summary>
public enum AssignmentStatus
{
    /// <summary>The assignment is current: its end date is empty, or today or later.</summary>
    Active,

    /// <summary>The assignment has ended, or has not yet begun.</summary>
    Inactive,
}
