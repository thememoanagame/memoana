namespace MemoAna.Common.Entities;

public class EntityBase(string Id)
{
    public string Id { get; set; } = Id ?? Guid.CreateVersion7().ToString();
}
