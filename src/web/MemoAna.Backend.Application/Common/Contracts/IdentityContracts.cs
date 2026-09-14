namespace MemoAna.Backend.Application.Common.Contracts;


/// <summary>Contains claim-type names used by InvisibleSP identity tokens.</summary>
public static class IdentityClaimTypes
{
    /// <summary>Gets the claim type used for application permissions.</summary>
    public const string Permission = "permission";
}

/// <summary>Contains authorization policy names used by the application.</summary>
public static class IdentityPolicies
{
    /// <summary>Gets the policy name required for administrator access.</summary>
    public const string Administrator = "system.admin";
    /// <summary>Gets the policy name required for user access</summary>
    public const string User = "system.user";
}

/// <summary>Contains role names used by the application.</summary>
public static class IdentityRoles
{
    /// <summary>Gets the policy name required for administrator access.</summary>
    public const string Administrator = "administrator";
    /// <summary>Gets the policy name required for user access</summary>
    public const string User = "user";
}


/// <summary>Contains token-type identifiers used by the JWT implementation.</summary>
public static class JwtTokenTypes
{
    /// <summary>Gets the token type for access tokens.</summary>
    public const string Access = "access";

    /// <summary>Gets the token type for refresh tokens.</summary>
    public const string Refresh = "refresh";
}
