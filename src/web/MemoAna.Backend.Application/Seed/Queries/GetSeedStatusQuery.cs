using Mediator;
using MemoAna.Backend.Application.Common.Responses;
using MemoAna.Backend.Application.Seed.Dtos;

namespace MemoAna.Backend.Application.Seed.Queries;

/// <summary>Gets the current availability of the seed middleware.</summary>
public sealed record GetSeedStatusQuery
    : IRequest<Response<SeedStatusDto>>;
