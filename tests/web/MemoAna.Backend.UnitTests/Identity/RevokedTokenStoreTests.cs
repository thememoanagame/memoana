using MemoAna.Backend.Infrastructure.Identity.Services;

namespace MemoAna.Backend.UnitTests.Identity;

/// <summary>Tests revoked token lifetime behavior.</summary>
public sealed class RevokedTokenStoreTests
{
    [Fact]
    public void IsRevoked_MissingToken_ReturnsFalse()
    {
        RevokedTokenStore store = new();

        Assert.False(store.IsRevoked("missing"));
    }

    [Fact]
    public void Revoke_FutureExpiration_ReturnsTrue()
    {
        RevokedTokenStore store = new();
        store.Revoke(
            "token",
            DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.True(store.IsRevoked("token"));
    }

    [Fact]
    public void Revoke_PastExpiration_DoesNotStore()
    {
        RevokedTokenStore store = new();
        store.Revoke(
            "token",
            DateTimeOffset.UtcNow.AddMinutes(-5));

        Assert.False(store.IsRevoked("token"));
    }

    [Fact]
    public void IsRevoked_ExpiredToken_RemovesToken()
    {
        RevokedTokenStore store = new();
        store.Revoke(
            "token",
            DateTimeOffset.UtcNow.AddMilliseconds(50));

        Assert.True(store.IsRevoked("token"));
        Thread.Sleep(100);
        Assert.False(store.IsRevoked("token"));
        Assert.False(store.IsRevoked("token"));
    }
}
