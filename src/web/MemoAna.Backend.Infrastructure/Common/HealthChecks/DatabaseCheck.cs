using MemoAna.Backend.Infrastructure.Persistence.Contexts;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MemoAna.Backend.Infrastructure.Common.HealthChecks;

public sealed class DatabaseCheck(PostgresDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken) ?
                HealthCheckResult.Healthy("Database is reachable",
                    new Dictionary<string, object>() { { "DatabaseInfo", new { CanConnect = true } } }) :
                    HealthCheckResult.Unhealthy("Database is not reachable",
                    null,
                    new Dictionary<string, object>() { { "DatabaseInfo", new { CanConnect = false } } });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Exception while checking database", ex);
        }
    }

}
