namespace MemoAna.Auth.Abstract;

/// <summary>Stores and checks revoked JWT identifiers until their expiration.</summary>
public interface IRevokedTokenStore
{
    /// <summary>Determines whether a token identifier is currently revoked.</summary>
    /// <param name="tokenId">The JWT identifier to check.</param>
    /// <returns><see langword="true"/> when the token is revoked; otherwise <see langword="false"/>.</returns>
    bool IsRevoked(string tokenId);

    /// <summary>Revokes a token identifier until the supplied expiration time.</summary>
    /// <param name="tokenId">The JWT identifier to revoke.</param>
    /// <param name="expiresAt">The time after which the revocation may be discarded.</param>
    void Revoke(string tokenId, DateTime expiresAt);
}
