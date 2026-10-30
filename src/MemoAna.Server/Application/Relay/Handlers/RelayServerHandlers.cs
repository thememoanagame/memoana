using System;
using MemoAna.Server.Application.Abstract;
using MemoAna.Server.Application.Relay.Requests;
using MemoAna.Server.Application.Relay.Responses;
using Mediator;
namespace MemoAna.Server.Application.Relay.Handlers;

public sealed class RelayServerHandlers(IRelayService relayService) 
: IRequestHandler<CreateRoomRequest, CreateRoomResponse>,
IRequestHandler<JoinRoomRequest, JoinRoomResponse>
{
    public async ValueTask<CreateRoomResponse> Handle(CreateRoomRequest request, CancellationToken cancellationToken)
    {
        // TODO: Implement the logic to create a room here
        // For now, we will return a dummy response
        return await relayService.CreateRoomAsync(request.PlayerId,request.RoomName, request.Password, request.Theme, request.Difficulty);
    }

    public async ValueTask<JoinRoomResponse> Handle(JoinRoomRequest request, CancellationToken cancellationToken)
    {
        // TODO: Implement the logic to join a room here
        // For now, we will return a dummy response
        return await relayService.JoinRoomAsync(request.PlayerId, request.RoomId, request.Password);
    }
}
