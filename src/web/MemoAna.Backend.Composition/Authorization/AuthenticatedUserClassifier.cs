using System.Security.Claims;
using MemoAna.Backend.Application.Common.Authorization;
using MemoAna.Backend.Application.Common.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace MemoAna.Backend.Composition.Authorization;

/// <summary>Classifies users through the existing server-side authorization policies.</summary>
public sealed class AuthenticatedUserClassifier(
    IAuthorizationService authorizationService) : IAuthenticatedUserClassifier
{
    /// <inheritdoc />
    public async Task<AuthenticatedUserType> ClassifyAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity?.IsAuthenticated != true)
        {
            return AuthenticatedUserType.Unauthenticated;
        }

        if ((await authorizationService.AuthorizeAsync(
                principal,
                IdentityPolicies.Administrator)).Succeeded)
        {
            return AuthenticatedUserType.SystemAdmin;
        }

        if ((await authorizationService.AuthorizeAsync(
                principal,
                IdentityPolicies.User)).Succeeded)
        {
            return AuthenticatedUserType.SystemUser;
        }

        return AuthenticatedUserType.Player;
    }
}
