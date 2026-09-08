using MemoAna.Backend.Application.Identity.Abstractions;
using MemoAna.Backend.Application.Identity.Commands;
using FluentValidation;

namespace MemoAna.Backend.Application.Identity.Validators;


/// <summary>Validates registration requests.</summary>
public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    /// <summary>Initializes registration validation rules.</summary>
    public RegisterCommandValidator(IIdentityService service)
    {
        _ = RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("The email cannot be empty.")
            .WithErrorCode("BAD_REQUEST")
            .EmailAddress()
            .WithErrorCode("UNPROCESSABLE_ENTITY")
            .WithMessage("The email address needs to be valid.")
            .MustAsync(async (email, ct) => !await service.EmailExistsAsync(email, ct))
            .WithErrorCode("CONFLICT")
            .WithMessage("The email is already in use");

        _ = RuleFor(x => x.Password)
            .NotEmpty()
            .WithErrorCode("BAD_REQUEST")
            .WithMessage("The new password cannot be null or empty.")
            .MinimumLength(8)
            .WithMessage("The new password needs to have at least 8 characters.")
            .WithErrorCode("UNPROCESSABLE_ENTITY")
            .Must(pwd => pwd.All(c => char.IsLetter(c) || char.IsDigit(c) || char.IsUpper(c) || char.IsLower(c) || char.IsSymbol(c)))
            .WithErrorCode("UNPROCESSABLE_ENTITY")
            .WithMessage("The password must have at least one letter upper and lower case, one digit and one special character");
    }
}

/// <summary>Validates login requests and prevents simultaneous second-factor credentials.</summary>
public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    /// <summary>Initializes login validation rules.</summary>
    public LoginCommandValidator()
    {
        _ = RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("The email cannot be empty.")
            .WithErrorCode("BAD_REQUEST")
            .EmailAddress()
            .WithErrorCode("UNPROCESSABLE_ENTITY")
            .WithMessage("The email address needs to be valid.");
        _ = RuleFor(x => x.Password)
            .NotEmpty()
            .WithErrorCode("BAD_REQUEST")
            .WithMessage("The new password cannot be null or empty.")
            .MinimumLength(8)
            .WithMessage("The new password needs to have at least 8 characters.")
            .WithErrorCode("UNPROCESSABLE_ENTITY")
            .Must(pwd => pwd.All(c => char.IsLetter(c) || char.IsDigit(c) || char.IsUpper(c) || char.IsLower(c) || char.IsSymbol(c)))
            .WithErrorCode("UNPROCESSABLE_ENTITY")
            .WithMessage("The password must have at least one letter upper and lower case, one digit and one special character");
        _ = RuleFor(x => x).Must(x => string.IsNullOrWhiteSpace(x.TwoFactorCode) || string.IsNullOrWhiteSpace(x.TwoFactorRecoveryCode))
            .WithMessage("Only one two-factor authentication code may be supplied.");
    }
}

/// <summary>Validates refresh-token requests.</summary>
public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    /// <summary>Initializes refresh-token validation rules.</summary>
    public RefreshTokenCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

/// <summary>Validates token-revocation requests.</summary>
public sealed class RevokeTokenCommandValidator : AbstractValidator<RevokeTokenCommand>
{
    /// <summary>Initializes token-revocation validation rules.</summary>
    public RevokeTokenCommandValidator() => RuleFor(x => x.AccessToken).NotEmpty();
}

/// <summary>Validates email-confirmation requests.</summary>
public sealed class ConfirmEmailCommandValidator : AbstractValidator<ConfirmEmailCommand>
{
    /// <summary>Initializes email-confirmation validation rules.</summary>
    public ConfirmEmailCommandValidator()
    {
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.Code).NotEmpty();
    }
}

/// <summary>Validates requests to resend email confirmation.</summary>
public sealed class ResendConfirmationEmailCommandValidator : AbstractValidator<ResendConfirmationEmailCommand>
{
    /// <summary>Initializes resend-confirmation validation rules.</summary>
    public ResendConfirmationEmailCommandValidator()
        => RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("The email cannot be empty.")
            .WithErrorCode("BAD_REQUEST")
            .EmailAddress()
            .WithErrorCode("UNPROCESSABLE_ENTITY")
            .WithMessage("The email address needs to be valid.");
}

/// <summary>Validates password recovery requests.</summary>
public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    /// <summary>Initializes password-recovery validation rules.</summary>
    public ForgotPasswordCommandValidator()
        => RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("The email cannot be empty.")
            .WithErrorCode("BAD_REQUEST")
            .EmailAddress()
            .WithErrorCode("UNPROCESSABLE_ENTITY")
            .WithMessage("The email address needs to be valid.");
} 

/// <summary>Validates password reset requests.</summary>
public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    /// <summary>Initializes password-reset validation rules.</summary>
    public ResetPasswordCommandValidator()
    {
        _ = RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("The email cannot be empty.")
            .WithErrorCode("BAD_REQUEST")
            .EmailAddress()
            .WithErrorCode("UNPROCESSABLE_ENTITY")
            .WithMessage("The email address needs to be valid.");
        _ = RuleFor(x => x.ResetCode)
            .NotEmpty()
            .WithErrorCode("BAD_REQUEST")
            .WithMessage("The reset code cannot be empty");
        _ = RuleFor(x => x
            .NewPassword)
            .NotEmpty()
            .WithErrorCode("BAD_REQUEST")
            .WithMessage("The new password cannot be null or empty.")
            .MinimumLength(8)
            .WithMessage("The new password needs to have at least 8 characters.")
            .WithErrorCode("UNPROCESSABLE_ENTITY")
            .Must(pwd => pwd.All(c => char.IsLetter(c) || char.IsDigit(c) || char.IsUpper(c) || char.IsLower(c) || char.IsSymbol(c)))
            .WithErrorCode("UNPROCESSABLE_ENTITY")
            .WithMessage("The password must have at least one letter upper and lower case, one digit and one special character");
    }
}

/// <summary>Validates identity information update requests.</summary>
public sealed class UpdateIdentityInfoCommandValidator : AbstractValidator<UpdateIdentityInfoCommand>
{
    /// <summary>Initializes identity-information validation rules.</summary>
    public UpdateIdentityInfoCommandValidator()
    {
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.OldPassword).NotEmpty();
        _ = RuleFor(x => x.NewEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.NewEmail));
        _ = RuleFor(x => x.NewPassword).MinimumLength(8).When(x => !string.IsNullOrWhiteSpace(x.NewPassword));
    }
}
 