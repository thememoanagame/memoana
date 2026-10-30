using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MemoAna.Server.Application.Relay.Responses;
using MemoAna.Server.Infrastructure.Services.Concrete;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoAna.Server.Tests;

public class RelayConcurrencyTests
{
    private static RelayService CreateRelayService()
    {
        return new RelayService(options: Options.Create(new RelayOptions { Port = 0 }));
    }

    [Fact]
    public async Task ConcurrentJoins_OnlyOneSucceeds_NeverMoreThanTwoPlayers()
    {
        await using var relay = CreateRelayService();

        var createResult = await relay.CreateRoomAsync("host", "Race Room", null, "Animals", 1);
        var roomId = createResult.Value!.RoomId;

        // 10 players race to join the same room simultaneously
        int attemptsCount = 10;
        var tasks = Enumerable.Range(0, attemptsCount)
            .Select(i => Task.Run(() => relay.JoinRoomAsync($"guest_{i}", roomId, null)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        int successCount = results.Count(r => r.Success && r.Status == RelayResultStatus.Success);
        int conflictCount = results.Count(r => !r.Success && r.Status == RelayResultStatus.Conflict);

        Assert.Equal(1, successCount);
        Assert.Equal(attemptsCount - 1, conflictCount);

        // Discovery list should show no available rooms
        var list = await relay.ListRoomsAsync();
        Assert.DoesNotContain(list.Value!, r => r.RoomId == roomId);
    }

    [Fact]
    public async Task ConcurrentCloseAndJoin_HandledSafelyWithoutCrashOrOrphan()
    {
        await using var relay = CreateRelayService();

        for (int i = 0; i < 20; i++)
        {
            var createResult = await relay.CreateRoomAsync("host", $"Race Room {i}", null, "Animals", 1);
            var roomId = createResult.Value!.RoomId;

            var joinTask = Task.Run(() => relay.JoinRoomAsync("guest", roomId, null));
            var closeTask = Task.Run(() => relay.CloseRoomAsync(roomId));

            await Task.WhenAll(joinTask, closeTask);

            // Join could succeed or fail depending on race, but room must ultimately be closed/not in list
            var list = await relay.ListRoomsAsync();
            Assert.DoesNotContain(list.Value!, r => r.RoomId == roomId);
        }
    }

    [Fact]
    public async Task ConcurrentLeaveAndJoin_HandledSafely()
    {
        await using var relay = CreateRelayService();

        var createResult = await relay.CreateRoomAsync("host", "Leave Race Room", null, "Animals", 1);
        var roomId = createResult.Value!.RoomId;

        // Guest 1 joins
        await relay.JoinRoomAsync("guest1", roomId, null);

        // Concurrently guest 1 leaves and guest 2 tries to join
        var leaveTask = Task.Run(() => relay.LeaveRoomAsync(roomId, "guest1"));
        var joinTask = Task.Run(() => relay.JoinRoomAsync("guest2", roomId, null));

        await Task.WhenAll(leaveTask, joinTask);

        // System state must remain valid and consistent
        var list = await relay.ListRoomsAsync();
        Assert.NotNull(list.Value);
    }
}

