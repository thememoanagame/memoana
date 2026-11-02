using MemoAna.Common.Abstract.Repositories;
using MemoAna.Common.Persistence;

namespace MemoAna.Common.Repositories;

/// <summary>
/// Coordinates EF Core persistence transactions for GameDbContext.
/// </summary>
public class UnitOfWork(GameDbContext dbContext) : UnitOfWork<GameDbContext>(dbContext)
{
}

