using Mediator;
using MemoAna.Backend.Application.Common.Responses;
using MemoAna.Backend.Application.Seed;
using MemoAna.Backend.Application.Seed.Commands;
using MemoAna.Backend.Application.Seed.Dtos;
using MemoAna.Backend.Application.Seed.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MemoAna.Backend.Controllers;

/// <summary>
/// Exposes the temporary seed endpoint until all prerequisites are initialized.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/seed")]
public sealed class SeedController(IMediator mediator) : ControllerBase
{
    /// <summary>Gets the current seed status.</summary>
    [HttpGet("status")]
    [ProducesResponseType<SeedStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(
        CancellationToken cancellationToken)
    {
        Response<SeedStatusDto> result = await mediator.Send(
            new GetSeedStatusQuery(),
            cancellationToken);
        return Ok(result.Data);
    }

    /// <summary>Executes the seed while the environment still requires it.</summary>
    [HttpPost]
    [ProducesResponseType<SeedOperationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<SeedStatusDto>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<Response<SeedOperationResultDto>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Seed(
        CancellationToken cancellationToken)
    {
        Response<SeedStatusDto> status = await mediator.Send(
            new GetSeedStatusQuery(),
            cancellationToken);
        if (status.Data is null || !status.Data.SeedRequired)
        {
            return Conflict(status.Data);
        }

        try
        {
            Response<SeedOperationResultDto> result = await mediator.Send(
                new SeedApplicationCommand(),
                cancellationToken);
            return Ok(result.Data);
        }
        catch (SeedException exception)
        {
            return BadRequest(Response.Failure<SeedOperationResultDto>(
                exception.Errors));
        }
    }
}
