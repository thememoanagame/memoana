using System; 
namespace MemoAna.Server.Application.Relay.Responses;

public sealed record CreateRoomResponse(string RoomId, string RoomName, string Theme, int Difficulty);
public sealed record JoinRoomResponse(string RoomId, string RoomName, string Theme, int Difficulty);