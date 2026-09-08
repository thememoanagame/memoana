using Mediator;
using MemoAna.Backend.Application.Health.Queries;
using Microsoft.AspNetCore.Mvc;

namespace MemoAna.Backend.Controllers.v1;

/// <summary>
/// Controller for health check endpoints.
/// Provides information about the application's health status, disk usage, database connectivity, and host information.
/// </summary>
/// <param name="mediator"></param>
[Route("api/v1/healthcheck")]
[ApiController]
[Tags("HealthChecks")]
public class HealthController(IMediator mediator) : ControllerBase
{
    [HttpGet("api")]
    public async Task<IActionResult> Check(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetHealthCheckQuery(), cancellationToken));

    [HttpGet("disk")]
    public async Task<IActionResult> DiskApi(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetApiDiskUsageQuery(), cancellationToken));

    [HttpGet("db")]
    public async Task<IActionResult> GetHealthReadyCheck(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetHealthReadyQuery(), cancellationToken));
    
    [HttpGet("info")]
    public async Task<IActionResult> GetHostInfo(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetHostInfoQuery(), cancellationToken));
}
