using MemoAna.Backend.Domain.Common;

namespace MemoAna.Backend.UnitTests.Common;

/// <summary>Tests common and MQTT domain models.</summary>
public sealed class DomainModelTests
{
    [Fact]
    public void EntityBase_GeneratesOrKeepsIdentifier()
    {
        TestEntity generated = new();
        TestEntity explicitId = new("explicit");

        Assert.False(string.IsNullOrWhiteSpace(generated.Id));
        Assert.Equal("explicit", explicitId.Id);
        Assert.NotEqual(default, generated.CreatedAt);
        Assert.NotEqual(default, generated.UpdatedAt);
        Assert.True(generated.UpdatedAt >= generated.CreatedAt);
    }
    private sealed class TestEntity(string id = "") : EntityBase(id);
}
