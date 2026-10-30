using System;
using MemoAna.Server.Application.Relay.Responses;

namespace MemoAna.Server.Application.Abstract;

public interface IRelayService
{
    Task<CreateRoomResponse> CreateRoomAsync(string playerId, string roomName, string password, string theme, int difficulty);
    Task<JoinRoomResponse> JoinRoomAsync(string playerId, string roomId, string password);
}
