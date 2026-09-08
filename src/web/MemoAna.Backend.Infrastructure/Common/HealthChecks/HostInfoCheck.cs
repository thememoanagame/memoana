using MemoAna.Backend.Application.Common.Utils;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MemoAna.Backend.Infrastructure.Common.HealthChecks;

public sealed class HostInfoCheck() : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            
            var check = new Dictionary<string, object>
            {
                {
                    "EnvironmentInfo",
                    new
                    {
                        EnvironmentName = Utils.GetEnvironment(),
                        UpTime = Utils.GetUpTime(),
                        OSVersion = Environment.OSVersion.ToString(),
                        HostName = Environment.MachineName
                    }
                }
            };
            return Task.FromResult(HealthCheckResult.Healthy("Host is up and running", check));
        }
        catch (Exception e)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Cannot retrieve host data", e, new Dictionary<string, object>(){ { "EnvironmentInfo", new {
                EnvironmentName = Utils.GetEnvironment(),
                ExceptionMessage = e.Message
            }}}));
        }
    }
}
