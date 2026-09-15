using Mediator;
using MemoAna.Backend.Application.Common.Abstractions;
using MemoAna.Backend.Application.Common.Responses;
using MemoAna.Backend.Application.Seed.Dtos;

namespace MemoAna.Backend.Application.Seed.Commands;

/// <summary>Explicitly initializes the fundamental application data.</summary>
public sealed record SeedApplicationCommand
    : ITransactionalRequest<Response<SeedOperationResultDto>>;
