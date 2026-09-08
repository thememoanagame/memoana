using Mediator;
using MemoAna.Backend.Application.Common.Responses;
using MemoAna.Backend.Application.Game.Commands;
using MemoAna.Backend.Application.Game.Dtos;
using MemoAna.Backend.Application.Game.Queries;
using MemoAna.Backend.Application.Game.Requests;
using Microsoft.AspNetCore.Mvc;

namespace MemoAna.Backend.Controllers.v1;

/// <summary>Exposes the GameData REST endpoints.</summary>
/// <param name="mediator">The application mediator.</param>
[ApiController]
[Route("api/v1/game-data")]
[Tags("GameData")]
public sealed class GameDataController(IMediator mediator) : ControllerBase
{
    /// <summary>Gets all complete card theme aggregates.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GameDataDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        Response<IReadOnlyList<GameDataDto>> result = await mediator.Send(
            new GetGameDataListQuery(),
            cancellationToken);

        return result.Succeeded
            ? Ok(result.Data)
            : StatusCode(StatusCodes.Status500InternalServerError, result);
    }

    /// <summary>Gets a complete card theme aggregate by identifier.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType<GameDataDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<Response<GameDataDto>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(
        string id,
        CancellationToken cancellationToken)
    {
        Response<GameDataDto> result = await mediator.Send(
            new GetGameDataQuery(id),
            cancellationToken);

        return result.Succeeded
            ? Ok(result.Data)
            : NotFound(result);
    }

    /// <summary>Creates a complete card theme aggregate.</summary>
    [HttpPost]
    [ProducesResponseType<GameDataDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create(
        GameDataRequest request,
        CancellationToken cancellationToken)
    {
        Response<GameDataDto> result = await mediator.Send(
            new CreateGameDataCommand(
                request.ThemeName,
                request.IsDefault,
                request.Base64Image),
            cancellationToken);

        if (!result.Succeeded)
        {
            return Conflict(result);
        }

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Data!.Id },
            result.Data);
    }

    /// <summary>Updates a complete card theme aggregate.</summary>
    [HttpPut("{id}")]
    [ProducesResponseType<GameDataDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Response<GameDataDto>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Update(
        string id,
        GameDataRequest request,
        CancellationToken cancellationToken)
    {
        Response<GameDataDto> result = await mediator.Send(
            new UpdateGameDataCommand(
                id,
                request.ThemeName,
                request.IsDefault,
                request.Base64Image),
            cancellationToken);

        return result.Succeeded
            ? Ok(result.Data)
            : NotFound(result);
    }

    /// <summary>Deletes a complete card theme aggregate.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Response<bool>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete(
        string id,
        CancellationToken cancellationToken)
    {
        Response<bool> result = await mediator.Send(
            new DeleteGameDataCommand(id),
            cancellationToken);

        return result.Succeeded
            ? NoContent()
            : NotFound(result);
    }
}
