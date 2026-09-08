using FluentValidation;
using MemoAna.Backend.Application.Game.Commands;
using MemoAna.Backend.Application.Game.Queries;

namespace MemoAna.Backend.Application.Game.Validators;

public sealed class CreateGameDataCommandValidator
    : AbstractValidator<CreateGameDataCommand>
{
    public CreateGameDataCommandValidator()
    {
        _ = RuleFor(command => command.ThemeName).NotEmpty();
        _ = RuleFor(command => command.Base64Image).NotEmpty();
    }
}

public sealed class GetGameDataQueryValidator
    : AbstractValidator<GetGameDataQuery>
{
    public GetGameDataQueryValidator() => RuleFor(query => query.Id).NotEmpty();
}

public sealed class UpdateGameDataCommandValidator
    : AbstractValidator<UpdateGameDataCommand>
{
    public UpdateGameDataCommandValidator()
    {
        _ = RuleFor(command => command.Id).NotEmpty();
        _ = RuleFor(command => command.ThemeName).NotEmpty();
        _ = RuleFor(command => command.Base64Image).NotEmpty();
    }
}

public sealed class DeleteGameDataCommandValidator
    : AbstractValidator<DeleteGameDataCommand>
{
    public DeleteGameDataCommandValidator() => RuleFor(command => command.Id).NotEmpty();
}
