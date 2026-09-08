using Mediator;
using MemoAna.Backend.Application.Common.Responses;
using MemoAna.Backend.Application.Game.Abstractions;
using MemoAna.Backend.Application.Game.Commands;
using MemoAna.Backend.Application.Game.Dtos;
using MemoAna.Backend.Application.Game.Queries;

namespace MemoAna.Backend.Application.Game.Handlers;

/// <summary>Handles GameData commands and queries through the application service.</summary>
public sealed class GameDataHandlers(IGameDataService gameDataService)
    : IRequestHandler<CreateGameDataCommand, Response<GameDataDto>>,
      IRequestHandler<GetGameDataQuery, Response<GameDataDto>>,
      IRequestHandler<GetGameDataListQuery, Response<IReadOnlyList<GameDataDto>>>,
      IRequestHandler<UpdateGameDataCommand, Response<GameDataDto>>,
      IRequestHandler<DeleteGameDataCommand, Response<bool>>
{
    public async ValueTask<Response<GameDataDto>> Handle(
        CreateGameDataCommand request,
        CancellationToken cancellationToken)
    {
        GameDataDto data = await gameDataService.CreateAsync(
            request.ThemeName,
            request.IsDefault,
            request.Base64Image,
            cancellationToken);
        return Response.Success(data);
    }

    public async ValueTask<Response<GameDataDto>> Handle(
        GetGameDataQuery request,
        CancellationToken cancellationToken)
    {
        GameDataDto? data = await gameDataService.GetByIdAsync(
            request.Id,
            cancellationToken);
        return data is null
            ? Response.Failure<GameDataDto>("Game data was not found.")
            : Response.Success(data);
    }

    public async ValueTask<Response<IReadOnlyList<GameDataDto>>> Handle(
        GetGameDataListQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GameDataDto> data = await gameDataService.ListAsync(
            cancellationToken);
        return Response.Success(data);
    }

    public async ValueTask<Response<GameDataDto>> Handle(
        UpdateGameDataCommand request,
        CancellationToken cancellationToken)
    {
        GameDataDto? data = await gameDataService.UpdateAsync(
            request.Id,
            request.ThemeName,
            request.IsDefault,
            request.Base64Image,
            cancellationToken);
        return data is null
            ? Response.Failure<GameDataDto>("Game data was not found.")
            : Response.Success(data);
    }

    public async ValueTask<Response<bool>> Handle(
        DeleteGameDataCommand request,
        CancellationToken cancellationToken)
    {
        bool deleted = await gameDataService.DeleteAsync(
            request.Id,
            cancellationToken);
        return deleted
            ? Response.Success(true)
            : Response.Failure<bool>("Game data was not found.");
    }
}
