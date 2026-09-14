using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// The <see cref="ICurrentUserContext"/> for a scope with no HTTP request — a Quartz job's scope.
/// </summary>
/// <remarks>
/// A background pass has a privilege tier but no personal identity, and the two are treated
/// differently on purpose. <see cref="Privileges"/> answers the empty set: a job resolves to the
/// least-privilege tier, so a Compass directory read from a job projects at Baseline and discloses no
/// tier-gated member. Identity is the opposite — <see cref="EdjeId"/>, <see cref="Email"/> and
/// <see cref="TpsEmployeeId"/> throw, because there is no caller and a port whose subject is the caller
/// (<c>ICallerDirectory</c>) must fail loudly rather than let "there is no caller" become "no match".
/// The real <c>CurrentUserContext</c> throws for both; this makes only privilege resolution
/// background-safe, which is what lets the weekly-summary job read <c>IDirectory</c> from Quartz.
/// </remarks>
public sealed class BackgroundUserContext : ICurrentUserContext
{
    private static InvalidOperationException NoCaller() =>
        new("No authenticated caller in a background scope");

    /// <inheritdoc/>
    public Guid EdjeId => throw NoCaller();

    /// <inheritdoc/>
    public string Email => throw NoCaller();

    /// <inheritdoc/>
    public string TpsEmployeeId => throw NoCaller();

    /// <inheritdoc/>
    public IReadOnlyList<string> Privileges => [];

    /// <inheritdoc/>
    public bool HasPrivilege(string privilege) => false;
}
