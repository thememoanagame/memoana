using System.Security.Claims;

namespace MemoAna.Backend.Application.Common.Authorization;

/// <summary>Identifies the authenticated user's application access group.</summary>
public enum AuthenticatedUserType
{
    Unauthenticated,
    Player,
    SystemAdmin,
    SystemUser
}

/// <summary>Classifies an authenticated principal using the application's authorization policies.</summary>
public interface IAuthenticatedUserClassifier
{
    /// <summary>Determines the access group for the supplied server-authenticated principal.</summary>
    Task<AuthenticatedUserType> ClassifyAsync(ClaimsPrincipal principal);
}
