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
        _ = RuleFor(command => command.Base64Images)
            .NotEmpty()
            .Must(images => images is not null
                && images.All(image => !string.IsNullOrWhiteSpace(image)));
    }
}

public sealed class GetGameDataQueryValidator
    : AbstractValidator<GetGameDataQuery>
{
    public GetGameDataQueryValidator() => RuleFor(query => query.Id).NotEmpty();
}

public sealed class GetGameDataByNameQueryValidator
    : AbstractValidator<GetGameDataByNameQuery>
{
    public GetGameDataByNameQueryValidator() =>
        RuleFor(query => query.ThemeName).NotEmpty();
}

public sealed class UpdateGameDataCommandValidator
    : AbstractValidator<UpdateGameDataCommand>
{
    public UpdateGameDataCommandValidator()
    {
        _ = RuleFor(command => command.Id).NotEmpty();
        _ = RuleFor(command => command.ThemeName).NotEmpty();
        _ = RuleFor(command => command.Base64Images)
            .NotEmpty()
            .Must(images => images is not null
                && images.All(image => !string.IsNullOrWhiteSpace(image)));
    }
}

public sealed class DeleteGameDataCommandValidator
    : AbstractValidator<DeleteGameDataCommand>
{
    public DeleteGameDataCommandValidator() => RuleFor(command => command.Id).NotEmpty();
}
