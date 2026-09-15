using Mediator;
using MemoAna.Backend.Application.Common.Responses;
using MemoAna.Backend.Application.Seed.Abstractions;
using MemoAna.Backend.Application.Seed.Commands;
using MemoAna.Backend.Application.Seed.Dtos;
using MemoAna.Backend.Application.Seed.Queries;

namespace MemoAna.Backend.Application.Seed.Handlers;

/// <summary>Handles the seed middleware commands and queries.</summary>
public sealed class SeedHandlers(IPostgresSeedService seedService)
    : IRequestHandler<GetSeedStatusQuery, Response<SeedStatusDto>>,
      IRequestHandler<SeedApplicationCommand, Response<SeedOperationResultDto>>
{
    /// <inheritdoc />
    public async ValueTask<Response<SeedStatusDto>> Handle(
        GetSeedStatusQuery request,
        CancellationToken cancellationToken)
    {
        return Response.Success(
            await seedService.GetStatusAsync(cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask<Response<SeedOperationResultDto>> Handle(
        SeedApplicationCommand request,
        CancellationToken cancellationToken)
    {
        return Response.Success(
            await seedService.SeedAsync(cancellationToken));
    }
}
