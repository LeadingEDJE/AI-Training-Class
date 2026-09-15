namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>The four Sales Dashboard tiles a breakdown can be requested for (AC-36, AC-37).</summary>
public enum DashboardCategory
{
    /// <summary>SOWs that are active — started and not ended.</summary>
    ActiveSows,

    /// <summary>SOWs expiring within 60 days with no follow-on (FR-008).</summary>
    ExpiringSows,

    /// <summary>EDJErs for whom every active assignment carries an end date (FR-011).</summary>
    ConfirmedRollouts,

    /// <summary>EDJErs holding an active assignment (FR-030) to any client (FR-014).</summary>
    Beach,
}
