using System.Threading.Tasks;
using MemoAna.Server.Application.Relay.Responses;
using MemoAna.Server.Infrastructure.Services.Concrete;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoAna.Server.Tests;

public class RelayAuthenticationTests
{
    private static RelayService CreateRelayService()
    {
        return new RelayService(options: Options.Create(new RelayOptions { Port = 0 }));
    }

    [Fact]
    public async Task RoomWithoutAuthentication_AllowsJoinWithoutPassword()
    {
        await using var relay = CreateRelayService();

        var createResult = await relay.CreateRoomAsync("host", "Open Room", null, "Animals", 1);
        Assert.False(createResult.Value!.RequiresPassword);

        var joinResult = await relay.JoinRoomAsync("guest", createResult.Value.RoomId, null);
        Assert.True(joinResult.Success);
        Assert.Equal(RelayResultStatus.Success, joinResult.Status);
    }

    [Fact]
    public async Task AuthenticatedRoom_RequiresCredential_RejectsNullOrEmptyPassword()
    {
        await using var relay = CreateRelayService();

        var createResult = await relay.CreateRoomAsync("host", "Secret Room", "Secret123", "Animals", 1);
        Assert.True(createResult.Value!.RequiresPassword);

        // Attempt without password
        var joinNull = await relay.JoinRoomAsync("guest", createResult.Value.RoomId, null);
        Assert.False(joinNull.Success);
        Assert.Equal(RelayResultStatus.Unauthorized, joinNull.Status);

        // Attempt with empty password
        var joinEmpty = await relay.JoinRoomAsync("guest", createResult.Value.RoomId, "");
        Assert.False(joinEmpty.Success);
        Assert.Equal(RelayResultStatus.Unauthorized, joinEmpty.Status);
    }

    [Fact]
    public async Task AuthenticatedRoom_AllowsJoinWithValidCredential()
    {
        await using var relay = CreateRelayService();

        var createResult = await relay.CreateRoomAsync("host", "Secret Room", "Secret123", "Animals", 1);

        var joinResult = await relay.JoinRoomAsync("guest", createResult.Value!.RoomId, "Secret123");
        Assert.True(joinResult.Success);
        Assert.Equal(RelayResultStatus.Success, joinResult.Status);
    }

    [Fact]
    public async Task AuthenticatedRoom_RejectsInvalidCredential()
    {
        await using var relay = CreateRelayService();

        var createResult = await relay.CreateRoomAsync("host", "Secret Room", "Secret123", "Animals", 1);

        var joinResult = await relay.JoinRoomAsync("guest", createResult.Value!.RoomId, "WrongPassword");
        Assert.False(joinResult.Success);
        Assert.Equal(RelayResultStatus.Unauthorized, joinResult.Status);
    }

    [Fact]
    public async Task InvalidAttempt_DoesNotConsumeVacancy()
    {
        await using var relay = CreateRelayService();

        var createResult = await relay.CreateRoomAsync("host", "Secret Room", "Secret123", "Animals", 1);
        var roomId = createResult.Value!.RoomId;

        // Several invalid attempts
        for (int i = 0; i < 3; i++)
        {
            var failedAttempt = await relay.JoinRoomAsync($"attacker_{i}", roomId, "WrongPassword");
            Assert.False(failedAttempt.Success);
            Assert.Equal(RelayResultStatus.Unauthorized, failedAttempt.Status);
        }

        // Room must still be available in discovery list
        var list = await relay.ListRoomsAsync();
        Assert.Contains(list.Value!, r => r.RoomId == roomId);

        // Valid player can still join
        var validJoin = await relay.JoinRoomAsync("legitimate_guest", roomId, "Secret123");
        Assert.True(validJoin.Success);
        Assert.Equal(RelayResultStatus.Success, validJoin.Status);
    }
}

