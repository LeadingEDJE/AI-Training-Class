using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Support;

/// <summary>
/// A minimal <see cref="ICurrentUserContext"/> for calling a service directly from a bare DI scope.
/// </summary>
/// <remarks>
/// <para>
/// The real <c>CurrentUserContext</c> reads <c>HttpContext.User</c>, so resolving it (or anything that
/// depends on it) from a scope with no HTTP request throws <c>InvalidOperationException: No HTTP
/// context</c>. <c>CompassAuditAtomicityTests</c> documents the other sanctioned fix for this — drive
/// the call over real HTTP instead — which is the right choice when the caller's identity or role is
/// itself under test. This class is for the opposite case: the test is about something else entirely
/// (which store answers, whether a read audits) and constructing the service directly, bypassing DI's
/// resolution of the real context, is the simpler fix.
/// </para>
/// <para>Holds no Compass role — Compass tier Baseline — since none of its current callers need one.</para>
/// </remarks>
public sealed class NoHttpContextCurrentUser : ICurrentUserContext
{
    /// <inheritdoc />
    public Guid EdjeId => Guid.Empty;

    /// <inheritdoc />
    public string Email => "no-http-context@example.test";

    /// <inheritdoc />
    public string TpsEmployeeId => string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<string> Privileges => [];

    /// <inheritdoc />
    public bool HasPrivilege(string privilege) => false;
}
