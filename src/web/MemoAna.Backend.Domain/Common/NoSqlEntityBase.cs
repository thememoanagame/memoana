namespace MemoAna.Backend.Domain.Common;

/// <summary>
/// Provides common persistence metadata for NoSQL domain entities.
/// </summary>
public abstract class NoSqlEntityBase(string id = "",
    DateTimeOffset? createdAt = null) : EntityBase(id, createdAt), INoSqlEntityBase;
