namespace LeadingEDJE.Leap.Api.Modules.Compass.Authorization;

/// <summary>
/// What a viewer may see across every Compass read surface — a ladder, not a set of flags.
/// </summary>
/// <remarks>
/// <see cref="SuperAdmin"/> implies <see cref="Elevated"/> implies <see cref="Baseline"/>, mirroring
/// <c>AddCompassAuthorization</c> where the Compass root satisfies the other three policies.
/// </remarks>
public enum CompassTier
{
    /// <summary>
    /// Authenticated, holding no Compass role — the implicit EDJEr base role (BR-2).
    /// </summary>
    /// <remarks>Not an error state: AC-5 and AC-12 both grant the directories to exactly this viewer.</remarks>
    Baseline,

    /// <summary>
    /// Compass Admin, Ops, or Sales. Sees inactive EDJErs, all SOWs, all notes, the rate indicator.
    /// </summary>
    /// <remarks>
    /// Compass Admin belongs here despite AC-44 making it read-only: BR-1 makes its
    /// visibility fully elevated. Capability and visibility are independent axes.
    /// </remarks>
    Elevated,

    /// <summary>Everything <see cref="Elevated"/> sees, plus Time Tracking Settings (AC-10) and the client panels (AC-14).</summary>
    SuperAdmin,
}

/// <summary>The disclosure questions BR-1 asks, answered once so every surface agrees.</summary>
/// <remarks>
/// These exist so no consumer writes <c>tier != CompassTier.Baseline</c> inline. A rule spelled out
/// at each of seven call sites is one that will eventually be spelled differently at one of them.
/// </remarks>
public static class CompassTierVisibility
{
    /// <summary>Whether the viewer may see former (inactive) EDJErs — AC-9, BR-1, on all three listings.</summary>
    public static bool SeesInactiveEdjers(this CompassTier tier) => tier >= CompassTier.Elevated;

    /// <summary>
    /// Whether the viewer may see Time Tracking Settings (AC-10) and the client panels (AC-14).
    /// </summary>
    /// <remarks>Super Admin only — the row "elevated means sees everything" intuition gets wrong.</remarks>
    public static bool SeesTimeTrackingSettings(this CompassTier tier) => tier == CompassTier.SuperAdmin;

    /// <summary>
    /// Whether the viewer may see other EDJErs' SOWs, notes, the rate indicator and View-SOW (AC-11, AC-16).
    /// </summary>
    /// <remarks>
    /// A baseline viewer's own record is a separate per-record decision (FR-018). Note AC-11's
    /// asymmetry: they see SOWs there, but never notes or the rate indicator.
    /// </remarks>
    public static bool SeesOthersSowsAndNotes(this CompassTier tier) => tier >= CompassTier.Elevated;
}
