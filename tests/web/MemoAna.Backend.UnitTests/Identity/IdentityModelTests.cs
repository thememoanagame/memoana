using MemoAna.Backend.Infrastructure.Identity.Models;

namespace MemoAna.Backend.UnitTests.Identity;

/// <summary>Tests Identity persistence models.</summary>
public sealed class IdentityModelTests
{
    [Fact]
    public void User_InitializesIdentityAndAuditValues()
    {
        User user = new("user@example.com");

        Assert.False(string.IsNullOrWhiteSpace(user.Id));
        Assert.Equal("user@example.com", user.UserName);
        Assert.NotEqual(default, user.CreatedAt);
        Assert.NotEqual(default, user.UpdatedAt);

        user.DisplayName = "User";
        user.FirstName = "Test";
        user.SurName = "User";
        user.CreatedBy = "system";
        user.UpdatedBy = "system";
        user.IsDeleted = true;
        user.DeletedAt = DateTimeOffset.UtcNow;

        Assert.True(user.IsDeleted);
        Assert.Equal("User", user.DisplayName);
        Assert.Equal("Test", user.FirstName);
        Assert.Equal("User", user.SurName);
    }

    [Fact]
    public void Role_InitializesNameAndAuditValues()
    {
        Role role = new("Administrator");

        Assert.Equal("Administrator", role.Name);
        Assert.NotEqual(default, role.CreatedAt);
        Assert.NotEqual(default, role.UpdatedAt);

        role.CreatedBy = "system";
        role.UpdatedBy = "system";
        role.IsDeleted = true;
        role.DeletedAt = DateTimeOffset.UtcNow;

        Assert.True(role.IsDeleted);
        Assert.Equal("system", role.CreatedBy);
        Assert.Equal("system", role.UpdatedBy);
        _ = Assert.NotNull(role.DeletedAt);
    }
}
