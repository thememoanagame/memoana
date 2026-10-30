using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using MemoAna.Game.Models;
using MemoAna.Server.Application.Abstract;
using MemoAna.Server.Application.Relay.Requests;
using MemoAna.Server.Application.Relay.Responses;

namespace MemoAna.Server.Application.Relay.Handlers;

public sealed class RelayServerHandlers(IRelayService relayService)
    : IRequestHandler<CreateRoomCommand, RelayResult<CreateRoomResponse>>,
      IRequestHandler<ListRoomsQuery, RelayResult<IReadOnlyList<RelayRoomSummary>>>,
      IRequestHandler<JoinRoomCommand, RelayResult<JoinRoomResponse>>,
      IRequestHandler<LeaveRoomCommand, RelayResult>,
      IRequestHandler<CloseRoomCommand, RelayResult>,
      IRequestHandler<CreateRoomRequest, RelayResult<CreateRoomResponse>>,
      IRequestHandler<JoinRoomRequest, RelayResult<JoinRoomResponse>>
{
    public async ValueTask<RelayResult<CreateRoomResponse>> Handle(CreateRoomCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlayerId))
            return RelayResult<CreateRoomResponse>.BadRequest("PlayerId is required.");

        if (string.IsNullOrWhiteSpace(request.RoomName))
            return RelayResult<CreateRoomResponse>.BadRequest("RoomName is required.");

        if (string.IsNullOrWhiteSpace(request.Theme))
            return RelayResult<CreateRoomResponse>.BadRequest("Theme is required.");

        if (request.Difficulty < 0)
            return RelayResult<CreateRoomResponse>.BadRequest("Difficulty must be non-negative.");

        return await relayService.CreateRoomAsync(
            request.PlayerId.Trim(),
            request.RoomName.Trim(),
            request.Password,
            request.Theme.Trim(),
            request.Difficulty,
            request.PlayerName?.Trim(),
            cancellationToken);
    }

    public async ValueTask<RelayResult<IReadOnlyList<RelayRoomSummary>>> Handle(ListRoomsQuery request, CancellationToken cancellationToken)
    {
        return await relayService.ListRoomsAsync(cancellationToken);
    }

    public async ValueTask<RelayResult<JoinRoomResponse>> Handle(JoinRoomCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RoomId))
            return RelayResult<JoinRoomResponse>.BadRequest("RoomId is required.");

        if (string.IsNullOrWhiteSpace(request.PlayerId))
            return RelayResult<JoinRoomResponse>.BadRequest("PlayerId is required.");

        return await relayService.JoinRoomAsync(
            request.PlayerId.Trim(),
            request.RoomId.Trim(),
            request.Password,
            request.PlayerName?.Trim(),
            cancellationToken);
    }

    public async ValueTask<RelayResult> Handle(LeaveRoomCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RoomId))
            return RelayResult.BadRequest("RoomId is required.");

        if (string.IsNullOrWhiteSpace(request.PlayerId))
            return RelayResult.BadRequest("PlayerId is required.");

        return await relayService.LeaveRoomAsync(request.RoomId.Trim(), request.PlayerId.Trim(), cancellationToken);
    }

    public async ValueTask<RelayResult> Handle(CloseRoomCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RoomId))
            return RelayResult.BadRequest("RoomId is required.");

        return await relayService.CloseRoomAsync(request.RoomId.Trim(), cancellationToken);
    }

    public async ValueTask<RelayResult<CreateRoomResponse>> Handle(CreateRoomRequest request, CancellationToken cancellationToken)
    {
        return await Handle(new CreateRoomCommand(request.PlayerId, request.RoomName, request.Password, request.Theme, request.Difficulty), cancellationToken);
    }

    public async ValueTask<RelayResult<JoinRoomResponse>> Handle(JoinRoomRequest request, CancellationToken cancellationToken)
    {
        return await Handle(new JoinRoomCommand(request.RoomId, request.PlayerId, request.Password), cancellationToken);
    }
}
