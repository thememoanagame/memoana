using MemoAna.Backend.Application.Health.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace MemoAna.Backend.Infrastructure.Common.Services;

public sealed class HealthService(HealthCheckService healthCheckService, ILogger<HealthService> logger) : IHealthService
{
  
    public async Task<HealthReport> PerformDbReadinessCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation($"{nameof(PerformDbReadinessCheckAsync)} starts");
            return await healthCheckService.CheckHealthAsync(r => r.Name == "DatabaseCheck", cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "{Message}", e.Message);
            throw;
        }
        finally
        {
            logger.LogInformation($"{nameof(PerformDbReadinessCheckAsync)} finishes");
        }
    }

    public async Task<HealthReport> GetApiDiskUsageAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation($"{nameof(GetApiDiskUsageAsync)} starts");
            return await healthCheckService.CheckHealthAsync(r => r.Name == "ApiDiskUsageCheck", cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "{Message}", e.Message);
            throw;
        }
        finally
        {
            logger.LogInformation($"{nameof(GetApiDiskUsageAsync)} ends");
        }
    }

    public async Task<HealthReport> GetHostInfoAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation($"{nameof(GetHostInfoAsync)} starts");
            return await healthCheckService.CheckHealthAsync(r => r.Name == "HostInfoCheck", cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "GetHostInfoAsync has errors: {Message}", e.Message);
            throw;
        }
        finally
        {
            logger.LogInformation($"{nameof(GetHostInfoAsync)} ends");
        }
    }

    public async Task<HealthReport> GetHealthCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation($"{nameof(GetHealthCheckAsync)} ends");
            return await healthCheckService.CheckHealthAsync(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "GetHealthCheckAsync has errors: {Message}", e.Message);
            throw;
        }
        finally
        {
            logger.LogInformation($"{nameof(GetHealthCheckAsync)} ends");
        }
    }
}