using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Vtt.Edge.Api;

internal sealed class EdgeDataProtectionHealthCheck(IDataProtectionProvider provider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = provider.CreateProtector("Vtt.Edge.Oidc.Readiness.v1").Protect("readiness");
            return Task.FromResult(HealthCheckResult.Healthy("OIDC correlation key ring is usable."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "OIDC correlation key ring is not usable.",
                exception));
        }
    }
}
