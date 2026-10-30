namespace MemoAna.Game.Models;

public enum RelayRoomStatus
{
    WaitingForGuest,
    Occupied,
    Closed,
    Expired
}

public sealed record RelayRoomSummary(
    string RoomId,
    string RoomName,
    string HostPlayerName,
    string Theme,
    int Difficulty,
    bool RequiresPassword,
    DateTime CreatedAt);

public sealed record CreateRoomDto(
    string PlayerId,
    string RoomName,
    string? Password,
    string Theme,
    int Difficulty,
    string? PlayerName = null);

public sealed record JoinRoomDto(
    string PlayerId,
    string? Password = null,
    string? PlayerName = null);

public sealed record LeaveRoomDto(
    string PlayerId);

public sealed record CreateRoomResponse(
    string RoomId,
    string RoomName,
    string Theme,
    int Difficulty,
    bool RequiresPassword = false,
    int RelayPort = 0,
    string ConnectionToken = "");

public sealed record JoinRoomResponse(
    string RoomId,
    string RoomName,
    string Theme,
    int Difficulty,
    int RelayPort = 0,
    string ConnectionToken = "");

