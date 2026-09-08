using Mediator;
using MemoAna.Backend.Application.Common.Abstractions;
using MemoAna.Backend.Application.Common.Responses;
using MemoAna.Backend.Application.Game.Dtos;

namespace MemoAna.Backend.Application.Game.Commands;

/// <summary>Creates a card theme and its asset.</summary>
public sealed record CreateGameDataCommand(
    string ThemeName,
    bool IsDefault,
    string Base64Image)
    : ITransactionalRequest<Response<GameDataDto>>;

/// <summary>Updates a card theme metadata and its asset.</summary>
public sealed record UpdateGameDataCommand(
    string Id,
    string ThemeName,
    bool IsDefault,
    string Base64Image)
    : ITransactionalRequest<Response<GameDataDto>>;

/// <summary>Deletes a card theme and its asset.</summary>
public sealed record DeleteGameDataCommand(string Id)
    : ITransactionalRequest<Response<bool>>;
