using FluentValidation;

namespace MemoAna.Backend.Components.Validators;

public class LoginRequestModelValidator : AbstractValidator<Pages.Identity.Login.LoginRequestModel>
{
    public LoginRequestModelValidator()
    {
        RuleFor(x=>x)
            .NotNull()
            .WithMessage("The Login data cannot be null")
            .NotEmpty()
            .WithMessage("The login data cannot be empty");

        RuleFor(x => x.Email)
            .EmailAddress()
            .WithMessage("The email provided must be a valid email address");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("A senha é obrigatória.")
            .MinimumLength(8).WithMessage("A senha deve ter pelo menos 8 caracteres.")
            .Matches(@"[A-Z]").WithMessage("A senha deve conter pelo menos uma letra maiúscula.")
            .Matches(@"[a-z]").WithMessage("A senha deve conter pelo menos uma letra minúscula.")
            .Matches(@"[0-9]").WithMessage("A senha deve conter pelo menos um número.")
            .Matches(@"[^a-zA-Z0-9]").WithMessage("A senha deve conter pelo menos um caractere especial.");
    }   
}
