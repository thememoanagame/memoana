using MemoAna.Backend.Domain.Game;
using MemoAna.Backend.Infrastructure.Common.Repository;
using MemoAna.Backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MemoAna.Backend.UnitTests.Common;

/// <summary>Tests the relational repository against the existing EF test provider.</summary>
public sealed class RepositoryTests
{
    [Fact]
    public async Task RepositorySupportsCrudListAndMissingEntities()
    {
        await using MemoAnaDbContext context = CreateContext();
        Repository<CardThemeEntity> repository = new(context);
        CardThemeEntity first = new()
        {
            ManifestId = "manifest-1"
        };
        CardThemeEntity second = new()
        {
            ManifestId = "manifest-2"
        };

        await repository.AddAsync(first);
        await repository.AddAsync(second);
        _ = await context.SaveChangesAsync();

        Assert.Equal(first, await repository.GetByIdAsync(first.Id));
        Assert.Equal(2, (await repository.ListAsync()).Count);

        first.ManifestId = "manifest-updated";
        Assert.True(await repository.UpdateAsync(first));
        _ = await context.SaveChangesAsync();
        Assert.Equal(
            "manifest-updated",
            (await repository.GetByIdAsync(first.Id))?.ManifestId);

        Assert.True(await repository.RemoveAsync(second.Id));
        _ = await context.SaveChangesAsync();
        Assert.Null(await repository.GetByIdAsync(second.Id));
        Assert.False(await repository.RemoveAsync(second.Id));
        Assert.Single(await repository.ListAsync(
            theme => theme.ManifestId == "manifest-updated"));
    }

    private static MemoAnaDbContext CreateContext()
    {
        DbContextOptions<MemoAnaDbContext> options =
            new DbContextOptionsBuilder<MemoAnaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        return new MemoAnaDbContext(options);
    }
}
