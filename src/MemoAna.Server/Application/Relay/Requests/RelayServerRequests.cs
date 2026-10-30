using System.Collections.Generic;
using Mediator;
using MemoAna.Game.Models;
using MemoAna.Server.Application.Relay.Responses;

namespace MemoAna.Server.Application.Relay.Requests;

public sealed record CreateRoomCommand(
    string PlayerId,
    string RoomName,
    string? Password,
    string Theme,
    int Difficulty,
    string? PlayerName = null) : IRequest<RelayResult<CreateRoomResponse>>;

public sealed record ListRoomsQuery() : IRequest<RelayResult<IReadOnlyList<RelayRoomSummary>>>;

public sealed record JoinRoomCommand(
    string RoomId,
    string PlayerId,
    string? Password,
    string? PlayerName = null) : IRequest<RelayResult<JoinRoomResponse>>;

public sealed record LeaveRoomCommand(
    string RoomId,
    string PlayerId) : IRequest<RelayResult>;

public sealed record CloseRoomCommand(
    string RoomId) : IRequest<RelayResult>;

// Backwards-compatible aliases if needed:
public sealed record CreateRoomRequest(
    string PlayerId,
    string RoomName,
    string? Password,
    string Theme,
    int Difficulty) : IRequest<RelayResult<CreateRoomResponse>>;

public sealed record JoinRoomRequest(
    string PlayerId,
    string RoomId,
    string? Password) : IRequest<RelayResult<JoinRoomResponse>>;
