using FluentValidation;
using MemoAna.Auth.Abstract;
using MemoAna.Auth.Requests;

namespace MemoAna.Server.Application.Auth.Validators;



/// <summary>Validates registration requests.</summary>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    /// <summary>Initializes registration validation rules.</summary>
    public RegisterRequestValidator(IIdentityService service)
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
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    /// <summary>Initializes login validation rules.</summary>
    public LoginRequestValidator()
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
public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    /// <summary>Initializes refresh-token validation rules.</summary>
    public RefreshTokenRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

/// <summary>Validates token-revocation requests.</summary>
public sealed class RevokeTokenRequestValidator : AbstractValidator<RevokeTokenRequest>
{
    /// <summary>Initializes token-revocation validation rules.</summary>
    public RevokeTokenRequestValidator() => RuleFor(x => x.AccessToken).NotEmpty();
}

/// <summary>Validates email-confirmation requests.</summary>
public sealed class ConfirmEmailRequestValidator : AbstractValidator<ConfirmEmailRequest>
{
    /// <summary>Initializes email-confirmation validation rules.</summary>
    public ConfirmEmailRequestValidator()
    {
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.Code).NotEmpty();
    }
}

/// <summary>Validates requests to resend email confirmation.</summary>
public sealed class ResendConfirmationEmailRequestValidator : AbstractValidator<ResendConfirmationEmailRequest>
{
    /// <summary>Initializes resend-confirmation validation rules.</summary>
    public ResendConfirmationEmailRequestValidator()
        => RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("The email cannot be empty.")
            .WithErrorCode("BAD_REQUEST")
            .EmailAddress()
            .WithErrorCode("UNPROCESSABLE_ENTITY")
            .WithMessage("The email address needs to be valid.");
}

/// <summary>Validates password recovery requests.</summary>
public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    /// <summary>Initializes password-recovery validation rules.</summary>
    public ForgotPasswordRequestValidator()
        => RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("The email cannot be empty.")
            .WithErrorCode("BAD_REQUEST")
            .EmailAddress()
            .WithErrorCode("UNPROCESSABLE_ENTITY")
            .WithMessage("The email address needs to be valid.");
}

/// <summary>Validates password reset requests.</summary>
public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    /// <summary>Initializes password-reset validation rules.</summary>
    public ResetPasswordRequestValidator()
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
public sealed class UpdateIdentityInfoRequestValidator : AbstractValidator<UpdateIdentityInfoRequest>
{
    /// <summary>Initializes identity-information validation rules.</summary>
    public UpdateIdentityInfoRequestValidator()
    {
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.OldPassword).NotEmpty();
        _ = RuleFor(x => x.NewEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.NewEmail));
        _ = RuleFor(x => x.NewPassword).MinimumLength(8).When(x => !string.IsNullOrWhiteSpace(x.NewPassword));
    }
}


