using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MemoAna.Game.Models;
using MemoAna.Server.Application.Relay.Responses;

namespace MemoAna.Server.Application.Abstract;

public interface IRelayService
{
    int Port { get; }

    Task<RelayResult<CreateRoomResponse>> CreateRoomAsync(
        string playerId,
        string roomName,
        string? password,
        string theme,
        int difficulty,
        string? playerName = null,
        CancellationToken cancellationToken = default);

    Task<RelayResult<IReadOnlyList<RelayRoomSummary>>> ListRoomsAsync(
        CancellationToken cancellationToken = default);

    Task<RelayResult<JoinRoomResponse>> JoinRoomAsync(
        string playerId,
        string roomId,
        string? password,
        string? playerName = null,
        CancellationToken cancellationToken = default);

    Task<RelayResult> LeaveRoomAsync(
        string roomId,
        string playerId,
        CancellationToken cancellationToken = default);

    Task<RelayResult> CloseRoomAsync(
        string roomId,
        CancellationToken cancellationToken = default);

    void CleanupExpiredRooms();
}
