using MemoAna.Backend.Infrastructure.Identity.Models;
using MemoAna.Backend.Infrastructure.Persistence;
using MemoAna.Backend.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace MemoAna.Backend.UnitTests.Common;

/// <summary>Tests database context persistence behaviors.</summary>
public sealed class PostgresDbContextTests
{
    [Fact]
    public async Task SaveChanges_HandlesIdentifiersAndSoftDelete()
    {
        await using PostgresDbContext context = CreateContext();
        User user = new("user@example.com");
        Role role = new("Operator");
        string userId = user.Id;
        string roleId = role.Id;
        _ = context.Users.Add(user);
        _ = context.Roles.Add(role);

        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(userId, user.Id);
        Assert.Equal(roleId, role.Id);

        _ = context.Users.Remove(user);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(user.IsDeleted);
        _ = Assert.NotNull(user.DeletedAt);
        User? persisted = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == user.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(persisted);
        Assert.True(persisted.IsDeleted);
    }

    [Fact]
    public async Task SaveChangesOverloads_ApplyPersistenceHooks()
    {
        await using PostgresDbContext context = CreateContext();

        Assert.Equal(0, context.SaveChanges());
        Assert.Equal(0, context.SaveChanges(true));
        Assert.Equal(0, await context.SaveChangesAsync(
            CancellationToken.None));
        Assert.Equal(0, await context.SaveChangesAsync(
            true, CancellationToken.None));
    }

    private static PostgresDbContext CreateContext()
    {
        DbContextOptions<PostgresDbContext> options =
            new DbContextOptionsBuilder<PostgresDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        return new PostgresDbContext(options);
    }
}
