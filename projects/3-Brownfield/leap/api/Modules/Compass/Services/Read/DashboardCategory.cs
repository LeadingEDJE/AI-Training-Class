namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>The four Sales Dashboard tiles a breakdown can be requested for (AC-36, AC-37).</summary>
public enum DashboardCategory
{
    /// <summary>SOWs that are active — started and not ended.</summary>
    ActiveSows,

    /// <summary>SOWs expiring within 90 days with no follow-on (FR-003).</summary>
    ExpiringSows,

    /// <summary>EDJErs for whom every active assignment carries an end date (FR-004).</summary>
    ConfirmedRollouts,

    /// <summary>EDJErs holding an active assignment (FR-032) to any internal client (FR-005).</summary>
    Beach,
}
