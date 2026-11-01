namespace MemoAna.Common.Entities;

public interface IEntityBase
{
    string Id { get; set; }
}

public class EntityBase(string Id) : IEntityBase
{
    public string Id { get; set; } = Id ?? Guid.CreateVersion7().ToString();
}
