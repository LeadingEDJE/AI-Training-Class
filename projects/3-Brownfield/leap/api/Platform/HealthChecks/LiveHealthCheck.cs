using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LeadingEDJE.Leap.Api.Platform.HealthChecks;

/// <summary>Liveness probe that always returns healthy, used by container orchestrators to confirm the process is running.</summary>
public class LiveHealthCheck : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HealthCheckResult.Healthy("alive"));
    }
}
