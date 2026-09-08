using Mediator;
using MemoAna.Backend.Application.Common.Responses;
using MemoAna.Backend.Application.Game.Dtos;

namespace MemoAna.Backend.Application.Game.Queries;

/// <summary>Gets a complete card theme aggregate by identifier.</summary>
public sealed record GetGameDataQuery(string Id)
    : IRequest<Response<GameDataDto>>;

/// <summary>Lists complete card theme aggregates available to the game.</summary>
public sealed record GetGameDataListQuery
    : IRequest<Response<IReadOnlyList<GameDataDto>>>;
