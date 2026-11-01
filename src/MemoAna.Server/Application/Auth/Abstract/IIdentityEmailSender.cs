namespace MemoAna.Server.Application.Auth.Abstract;

/// <summary>Sends email messages required by identity workflows.</summary>
public interface IIdentityEmailSender
{
    /// <summary>Sends an email confirmation message.</summary>
    /// <param name="email">The recipient email address.</param>
    /// <param name="confirmationLink">The confirmation link to include in the message.</param>
    /// <param name="cancellationToken">The token used to cancel the send operation.</param>
    /// <returns>A task representing the send operation.</returns>
    Task SendConfirmationAsync(string email, string confirmationLink, CancellationToken cancellationToken);

    /// <summary>Sends a password reset message.</summary>
    /// <param name="email">The recipient email address.</param>
    /// <param name="resetLink">The password reset link to include in the message.</param>
    /// <param name="cancellationToken">The token used to cancel the send operation.</param>
    /// <returns>A task representing the send operation.</returns>
    Task SendPasswordResetAsync(string email, string resetLink, CancellationToken cancellationToken);
}
