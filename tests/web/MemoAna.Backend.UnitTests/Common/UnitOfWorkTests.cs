using MemoAna.Backend.Infrastructure.Common.UnitOfWork;
using MemoAna.Backend.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MemoAna.Backend.UnitTests.Common;

/// <summary>Tests database unit-of-work transactions.</summary>
public sealed class UnitOfWorkTests
{
    [Fact]
    public async Task UnitOfWork_CommitsAndRollsBackTransactions()
    {
        await using SqliteConnection connection =
            new("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        DbContextOptions<MemoAnaDbContext> options =
            new DbContextOptionsBuilder<MemoAnaDbContext>()
                .UseSqlite(connection)
                .Options;
        await using MemoAnaDbContext context =
            new(options);
        _ = await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await using UnitOfWork unitOfWork =
            new(context);

        await unitOfWork.CommitTransactionAsync(
            TestContext.Current.CancellationToken);
        await unitOfWork.RollbackTransactionAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(0, await unitOfWork.SaveChangesAsync(
            TestContext.Current.CancellationToken));

        await unitOfWork.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        await unitOfWork.CommitTransactionAsync(
            TestContext.Current.CancellationToken);

        await unitOfWork.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        await unitOfWork.RollbackTransactionAsync(
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UnitOfWork_SecondBegin_DoesNotReplaceTransaction()
    {
        await using SqliteConnection connection =
            new("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        DbContextOptions<MemoAnaDbContext> options =
            new DbContextOptionsBuilder<MemoAnaDbContext>()
                .UseSqlite(connection)
                .Options;
        await using MemoAnaDbContext context =
            new(options);
        _ = await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await using UnitOfWork unitOfWork =
            new(context);

        await unitOfWork.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        await unitOfWork.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        await unitOfWork.RollbackTransactionAsync(
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UnitOfWork_DisposeRollsBackActiveTransaction()
    {
        await using SqliteConnection connection =
            new("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        DbContextOptions<MemoAnaDbContext> options =
            new DbContextOptionsBuilder<MemoAnaDbContext>()
                .UseSqlite(connection)
                .Options;
        await using MemoAnaDbContext context =
            new(options);
        _ = await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        UnitOfWork unitOfWork = new(context);

        await unitOfWork.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        await unitOfWork.DisposeAsync();
    }
}
