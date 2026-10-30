using System;
using Mediator;
using MemoAna.Server.Application.Relay.Responses; 
namespace MemoAna.Server.Application.Relay.Requests;
    
public sealed record CreateRoomRequest(string PlayerId, string RoomName, string Password, string Theme, int Difficulty) 
: IRequest<CreateRoomResponse>;

public sealed record JoinRoomRequest(string PlayerId, string RoomId, string Password) : IRequest<JoinRoomResponse>;