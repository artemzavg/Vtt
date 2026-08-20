using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Vtt.Identity.Api;

internal sealed class IdentityDataProtectionHealthCheck(IDataProtectionProvider provider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = provider.CreateProtector("Vtt.Identity.Readiness.v1").Protect("readiness");
            return Task.FromResult(HealthCheckResult.Healthy("Data Protection key ring is usable."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Data Protection key ring is not usable.",
                exception));
        }
    }
}
