using System;
using MemoAna.Server.Application.Abstract;
using MemoAna.Server.Application.Relay.Responses;
namespace MemoAna.Server.Infrastructure.Services.Concrete;

public class RelayService : IRelayService
{
 
    public Task<CreateRoomResponse> CreateRoomAsync(string playerId, string roomName, string password, string theme, int difficulty)
    {
        return Task.FromResult(new CreateRoomResponse(
            RoomId: Guid.NewGuid().ToString(),
            RoomName: roomName,
            Theme: theme,
            Difficulty: difficulty));
    }

    public Task<JoinRoomResponse> JoinRoomAsync(string playerId, string roomId, string password)
    {
        return Task.FromResult(new JoinRoomResponse(
            RoomId: roomId,
            RoomName: "Sample Room",
            Theme: "Sample Theme",
            Difficulty: 1));
    }
}