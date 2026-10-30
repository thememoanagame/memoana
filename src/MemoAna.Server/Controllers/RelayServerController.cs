using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using MemoAna.Game.Models;
using MemoAna.Server.Application.Relay.Requests;
using MemoAna.Server.Application.Relay.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MemoAna.Server.Controllers;

[Route("api/v1/relay")]
[ApiController]
[Tags("Relay Server")]
public class RelayServerController(IMediator mediator) : ControllerBase
{
    [HttpPost("rooms")]
    [ProducesResponseType(typeof(CreateRoomResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomDto dto, CancellationToken cancellationToken)
    {
        var command = new CreateRoomCommand(
            dto.PlayerId,
            dto.RoomName,
            dto.Password,
            dto.Theme,
            dto.Difficulty,
            dto.PlayerName);

        var result = await mediator.Send(command, cancellationToken);
        if (!result.Success)
            return ToActionResult(result);

        return CreatedAtAction(nameof(ListRooms), null, result.Value);
    }

    [HttpGet("rooms")]
    [ProducesResponseType(typeof(IReadOnlyList<RelayRoomSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRooms(CancellationToken cancellationToken)
    {
        var query = new ListRoomsQuery();
        var result = await mediator.Send(query, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("rooms/{roomId}/join")]
    [ProducesResponseType(typeof(JoinRoomResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> JoinRoom(
        [FromRoute] string roomId,
        [FromBody] JoinRoomDto dto,
        CancellationToken cancellationToken)
    {
        var command = new JoinRoomCommand(
            roomId,
            dto.PlayerId,
            dto.Password,
            dto.PlayerName);

        var result = await mediator.Send(command, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("rooms/{roomId}/leave")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LeaveRoom(
        [FromRoute] string roomId,
        [FromBody] LeaveRoomDto dto,
        CancellationToken cancellationToken)
    {
        var command = new LeaveRoomCommand(roomId, dto.PlayerId);
        var result = await mediator.Send(command, cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("rooms/{roomId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CloseRoom(
        [FromRoute] string roomId,
        CancellationToken cancellationToken)
    {
        var command = new CloseRoomCommand(roomId);
        var result = await mediator.Send(command, cancellationToken);
        return ToActionResult(result);
    }

    private IActionResult ToActionResult<T>(RelayResult<T> result)
    {
        if (result.Success)
            return Ok(result.Value);

        return result.Status switch
        {
            RelayResultStatus.NotFound => NotFound(new { message = result.Message }),
            RelayResultStatus.Unauthorized => StatusCode(StatusCodes.Status401Unauthorized, new { message = result.Message }),
            RelayResultStatus.Conflict => Conflict(new { message = result.Message }),
            RelayResultStatus.BadRequest => BadRequest(new { message = result.Message }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { message = result.Message })
        };
    }

    private IActionResult ToActionResult(RelayResult result)
    {
        if (result.Success)
            return NoContent();

        return result.Status switch
        {
            RelayResultStatus.NotFound => NotFound(new { message = result.Message }),
            RelayResultStatus.Unauthorized => StatusCode(StatusCodes.Status401Unauthorized, new { message = result.Message }),
            RelayResultStatus.Conflict => Conflict(new { message = result.Message }),
            RelayResultStatus.BadRequest => BadRequest(new { message = result.Message }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { message = result.Message })
        };
    }
}
