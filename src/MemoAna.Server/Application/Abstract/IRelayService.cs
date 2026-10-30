using System;

namespace MemoAna.Server.Application.Abstract;

public interface IRelayService
{
    public Task<CreateRoomResponse> CreateRoomAsync(string playerId, string roomName, string password, string theme, int difficulty);
    public Task<JoinRoomResponse> JoinRoomAsync(string playerId, string roomId, string password);
}
