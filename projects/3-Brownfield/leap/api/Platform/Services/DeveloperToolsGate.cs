using Microsoft.Extensions.Configuration;

namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// Decides whether the destructive developer-tools surface may exist in this process at all.
/// </summary>
/// <remarks>
/// Guards a button that irreversibly destroys data, so it fails closed twice over: the opt-in
/// configuration flag must be present and <c>true</c>, and the environment must not be Production.
/// Not <c>IHostEnvironment.IsDevelopment()</c> — only local runs as <c>Development</c>, while deployed
/// dev and every PR preview run <c>Staging</c>, so an <c>IsDevelopment()</c> gate would exclude the
/// environment this exists for, and flipping <c>ASPNETCORE_ENVIRONMENT</c> to fix that would also
/// switch on <c>/auth/stub-login</c> and DevBypass. The Production refusal survives because
/// configuration can be wrong — <c>helm --set</c> beats <c>-f</c> and chart values merge key by key —
/// and a hard refusal keyed on the environment name costs one comparison.
/// </remarks>
public static class DeveloperToolsGate
{
    /// <summary>The configuration key that opts an environment in. Absent or unparseable means OFF.</summary>
    public const string EnabledKey = "DeveloperTools:Enabled";

    /// <summary>
    /// Whether the developer-tools routes may be mapped in this process.
    /// </summary>
    /// <param name="configuration">Application configuration, read for <see cref="EnabledKey"/>.</param>
    /// <param name="environmentName">
    /// The host environment name, compared against <c>Production</c> case-insensitively. Passed as a
    /// string rather than an <c>IHostEnvironment</c> so this stays a pure function that a unit test
    /// can drive through every combination without a host.
    /// </param>
    /// <returns><c>true</c> only when the flag is explicitly <c>true</c> AND the environment is not Production.</returns>
    public static bool IsEnabled(IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Inverted default, and that is the point: a missing, empty or malformed value is NOT consent
        // to expose a truncate button. Only the literal string "true" opens this.
        if (!bool.TryParse(configuration[EnabledKey], out var enabled) || !enabled)
        {
            return false;
        }

        return !IsProductionName(environmentName);
    }

    /// <summary>
    /// Whether configuration asks for developer tools in an environment that must never have them —
    /// the one state worth logging loudly, because it means a real misconfiguration reached a host.
    /// </summary>
    /// <param name="configuration">Application configuration, read for <see cref="EnabledKey"/>.</param>
    /// <param name="environmentName">The host environment name.</param>
    /// <returns><c>true</c> when the flag is on and the environment is Production.</returns>
    public static bool IsSuppressedByProduction(IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return bool.TryParse(configuration[EnabledKey], out var enabled)
            && enabled
            && IsProductionName(environmentName);
    }

    private static bool IsProductionName(string environmentName) =>
        string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);
}
