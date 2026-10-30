using System;
using System.Linq;
using System.Threading.Tasks;
using MemoAna.Game.Models;
using MemoAna.Server.Application.Relay.Responses;
using MemoAna.Server.Infrastructure.Services.Concrete;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoAna.Server.Tests;

public class RelayRoomTests
{
    private static RelayService CreateRelayService(RelayOptions? options = null)
    {
        var opt = options ?? new RelayOptions { Port = 0 }; // ephemeral port
        return new RelayService(options: Options.Create(opt));
    }

    [Fact]
    public async Task CreateRoom_ShouldCreateRoomWithUniqueIdAndReturnConnectionData()
    {
        await using var relay = CreateRelayService();

        var result = await relay.CreateRoomAsync("player1", "Room 1", null, "Animals", 1, "Alice");

        Assert.True(result.Success);
        Assert.Equal(RelayResultStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.RoomId));
        Assert.Equal("Room 1", result.Value.RoomName);
        Assert.Equal("Animals", result.Value.Theme);
        Assert.Equal(1, result.Value.Difficulty);
        Assert.False(result.Value.RequiresPassword);
        Assert.True(result.Value.RelayPort > 0);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.ConnectionToken));
    }

    [Fact]
    public async Task ListRooms_ShouldOnlyReturnAvailableRooms()
    {
        await using var relay = CreateRelayService();

        var room1 = await relay.CreateRoomAsync("player1", "Room 1", null, "Animals", 1);
        var room2 = await relay.CreateRoomAsync("player2", "Room 2", null, "Space", 2);

        var listResult = await relay.ListRoomsAsync();
        Assert.True(listResult.Success);
        Assert.Equal(2, listResult.Value!.Count);

        // Join room 1 so it becomes full
        await relay.JoinRoomAsync("player3", room1.Value!.RoomId, null);

        // Close room 2
        await relay.CloseRoomAsync(room2.Value!.RoomId);

        // Now neither room 1 (full) nor room 2 (closed) should be listed
        var listAfter = await relay.ListRoomsAsync();
        Assert.True(listAfter.Success);
        Assert.Empty(listAfter.Value!);
    }

    [Fact]
    public async Task JoinRoom_ShouldAllowGuestAndMarkRoomAsOccupied()
    {
        await using var relay = CreateRelayService();

        var createResult = await relay.CreateRoomAsync("host1", "Room 1", null, "Animals", 1);
        var roomId = createResult.Value!.RoomId;

        var joinResult = await relay.JoinRoomAsync("guest1", roomId, null, "Bob");

        Assert.True(joinResult.Success);
        Assert.Equal(RelayResultStatus.Success, joinResult.Status);
        Assert.NotNull(joinResult.Value);
        Assert.Equal(roomId, joinResult.Value.RoomId);
        Assert.False(string.IsNullOrWhiteSpace(joinResult.Value.ConnectionToken));
        Assert.NotEqual(createResult.Value.ConnectionToken, joinResult.Value.ConnectionToken);

        // Room is no longer in discovery list
        var list = await relay.ListRoomsAsync();
        Assert.DoesNotContain(list.Value!, r => r.RoomId == roomId);
    }

    [Fact]
    public async Task JoinRoom_ShouldPreventThirdPlayerWithConflict()
    {
        await using var relay = CreateRelayService();

        var createResult = await relay.CreateRoomAsync("host1", "Room 1", null, "Animals", 1);
        var roomId = createResult.Value!.RoomId;

        // Player B joins
        var joinB = await relay.JoinRoomAsync("guestB", roomId, null);
        Assert.True(joinB.Success);

        // Player C tries to join -> Conflict
        var joinC = await relay.JoinRoomAsync("guestC", roomId, null);
        Assert.False(joinC.Success);
        Assert.Equal(RelayResultStatus.Conflict, joinC.Status);
    }

    [Fact]
    public async Task LeaveRoom_WhenHostLeaves_ShouldCloseRoom()
    {
        await using var relay = CreateRelayService();

        var createResult = await relay.CreateRoomAsync("host1", "Room 1", null, "Animals", 1);
        var roomId = createResult.Value!.RoomId;

        await relay.JoinRoomAsync("guest1", roomId, null);

        // Host leaves
        var leaveResult = await relay.LeaveRoomAsync(roomId, "host1");
        Assert.True(leaveResult.Success);

        // Room should no longer exist
        var joinAgain = await relay.JoinRoomAsync("guest2", roomId, null);
        Assert.Equal(RelayResultStatus.NotFound, joinAgain.Status);
    }

    [Fact]
    public async Task LeaveRoom_WhenGuestLeaves_ShouldFreeSlot()
    {
        await using var relay = CreateRelayService();

        var createResult = await relay.CreateRoomAsync("host1", "Room 1", null, "Animals", 1);
        var roomId = createResult.Value!.RoomId;

        await relay.JoinRoomAsync("guest1", roomId, null);

        // Guest leaves
        var leaveResult = await relay.LeaveRoomAsync(roomId, "guest1");
        Assert.True(leaveResult.Success);

        // Room should now be available again in discovery list
        var list = await relay.ListRoomsAsync();
        Assert.Contains(list.Value!, r => r.RoomId == roomId);

        // Another guest can join
        var joinAgain = await relay.JoinRoomAsync("guest2", roomId, null);
        Assert.True(joinAgain.Success);
    }

    [Fact]
    public async Task CloseRoom_ShouldRemoveRoomAndReleaseResources()
    {
        await using var relay = CreateRelayService();

        var createResult = await relay.CreateRoomAsync("host1", "Room 1", null, "Animals", 1);
        var roomId = createResult.Value!.RoomId;

        var closeResult = await relay.CloseRoomAsync(roomId);
        Assert.True(closeResult.Success);

        // Cannot join closed room
        var joinResult = await relay.JoinRoomAsync("guest1", roomId, null);
        Assert.Equal(RelayResultStatus.NotFound, joinResult.Status);

        // Not in list
        var list = await relay.ListRoomsAsync();
        Assert.Empty(list.Value!);
    }

    [Fact]
    public async Task NonExistentRoom_ShouldReturnNotFound()
    {
        await using var relay = CreateRelayService();

        var joinResult = await relay.JoinRoomAsync("player", "non-existent-id", null);
        Assert.Equal(RelayResultStatus.NotFound, joinResult.Status);

        var leaveResult = await relay.LeaveRoomAsync("non-existent-id", "player");
        Assert.Equal(RelayResultStatus.NotFound, leaveResult.Status);

        var closeResult = await relay.CloseRoomAsync("non-existent-id");
        Assert.Equal(RelayResultStatus.NotFound, closeResult.Status);
    }

    [Fact]
    public async Task ExpiredRoom_ShouldBeCleanedUpAndNotJoinable()
    {
        // 0-minute timeout so rooms expire immediately
        var options = new RelayOptions
        {
            Port = 0,
            InactivityTimeoutMinutes = 0,
            UnconnectedRoomTimeoutMinutes = 0,
            CleanupIntervalSeconds = 60
        };
        await using var relay = CreateRelayService(options);

        var createResult = await relay.CreateRoomAsync("host1", "Room 1", null, "Animals", 1);
        var roomId = createResult.Value!.RoomId;

        // Force cleanup
        relay.CleanupExpiredRooms();

        // Listing should be empty
        var list = await relay.ListRoomsAsync();
        Assert.Empty(list.Value!);

        // Joining should return NotFound
        var join = await relay.JoinRoomAsync("guest1", roomId, null);
        Assert.Equal(RelayResultStatus.NotFound, join.Status);
    }
}

