using MemoAna.Common.Abstract.Repositories;
using MemoAna.Common.Entities;
using MemoAna.Common.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MemoAna.Common.Repositories;

/// <summary>
/// Provides EF Core persistence operations for GameDbContext.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public class Repository<TEntity> : Repository<TEntity, GameDbContext>
    where TEntity : EntityBase
{
    [ActivatorUtilitiesConstructor]
    public Repository(ILogger<Repository<TEntity>> logger, IUnitOfWork<GameDbContext> uow)
        : base(logger, uow)
    {
    }

    public Repository(GameDbContext dbContext, ILogger<Repository<TEntity>> logger, IUnitOfWork<GameDbContext> uow)
        : base(logger, uow)
    {
    }
}

