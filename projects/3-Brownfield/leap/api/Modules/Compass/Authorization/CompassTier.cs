namespace LeadingEDJE.Leap.Api.Modules.Compass.Authorization;

/// <summary>
/// What a viewer may see across every Compass read surface — a ladder, not a set of flags.
/// </summary>
public enum CompassTier
{
    /// <summary>
    /// Authenticated, holding no Compass role — the implicit EDJEr base role (BR-2).
    /// </summary>
    /// <remarks>Is an error state in most contexts: AC-5 and AC-12 are exceptions that grant limited directory access to this viewer.</remarks>
    Baseline,

    /// <summary>
    /// Compass Admin, Ops, or Sales. Sees inactive EDJErs, all SOWs, all notes, the rate indicator.
    /// </summary>
    /// <remarks>
    /// Compass Admin does not belong here: AC-44 keeps its visibility at the lowest tier since BR-1
    /// ties visibility directly to write capability, unlike the other two elevated roles.
    /// </remarks>
    Elevated,

    /// <summary>Everything <see cref="Elevated"/> sees, plus Time Tracking Settings (AC-10) and the client panels (AC-14).</summary>
    SuperAdmin,
}

/// <summary>The disclosure questions BR-1 asks, answered once so every surface agrees.</summary>
public static class CompassTierVisibility
{
    /// <summary>Whether the viewer may see former (inactive) EDJErs — AC-9, BR-1, on all three listings.</summary>
    public static bool SeesInactiveEdjers(this CompassTier tier) => tier >= CompassTier.Elevated;

    /// <summary>
    /// Whether the viewer may see Time Tracking Settings (AC-10) and the client panels (AC-14).
    /// </summary>
    public static bool SeesTimeTrackingSettings(this CompassTier tier) => tier == CompassTier.SuperAdmin;

    /// <summary>
    /// Whether the viewer may see other EDJErs' SOWs, notes, the rate indicator and View-SOW (AC-11, AC-16).
    /// </summary>
    /// <remarks>
    /// A baseline viewer's own record follows the exact same rule as everyone else's (FR-018).
    /// AC-11 grants no asymmetry: they see the same notes and rate indicator everywhere else does.
    /// </remarks>
    public static bool SeesOthersSowsAndNotes(this CompassTier tier) => tier >= CompassTier.Elevated;
}
