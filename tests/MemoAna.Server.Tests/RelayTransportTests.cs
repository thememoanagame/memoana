using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;
using MemoAna.Server.Infrastructure.Services.Concrete;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoAna.Server.Tests;

public class RelayTransportTests
{
    private static RelayService CreateRelayService()
    {
        return new RelayService(options: Options.Create(new RelayOptions { Port = 0 }));
    }

    private sealed class TestClient : IAsyncDisposable
    {
        public NetManager Manager { get; }
        public EventBasedNetListener Listener { get; }
        public NetPeer? Peer { get; private set; }
        public bool IsConnected => Peer?.ConnectionState == ConnectionState.Connected;

        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loopTask;

        public TestClient()
        {
            Listener = new EventBasedNetListener();
            Listener.PeerConnectedEvent += p => Peer = p;
            Listener.PeerDisconnectedEvent += (p, _) =>
            {
                if (Peer == p)
                    Peer = null;
            };

            Manager = new NetManager(Listener) { AutoRecycle = true };
            Manager.Start(0);

            _loopTask = Task.Run(async () =>
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        Manager.PollEvents();
                        await Task.Delay(5, _cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        public async Task<bool> ConnectAsync(int port, string key, TimeSpan timeout)
        {
            Manager.Connect("127.0.0.1", port, key);
            DateTime deadline = DateTime.UtcNow + timeout;
            while (!IsConnected && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
            return IsConnected;
        }

        public void Send(byte[] data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered)
        {
            Peer?.Send(data, 0, deliveryMethod);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                await _loopTask;
            }
            catch
            {
            }
            _cts.Dispose();
            Peer?.Disconnect();
            Manager.Stop();
        }
    }

    [Fact]
    public async Task Relay_ForwardsOpaquePayload_FromAToB_AndFromBToA()
    {
        await using var relay = CreateRelayService();
        int relayPort = relay.Port;

        var createResult = await relay.CreateRoomAsync("playerA", "Relay Room", null, "Animals", 1);
        string roomId = createResult.Value!.RoomId;
        string tokenA = createResult.Value.ConnectionToken;

        var joinResult = await relay.JoinRoomAsync("playerB", roomId, null);
        string tokenB = joinResult.Value!.ConnectionToken;

        await using var clientA = new TestClient();
        await using var clientB = new TestClient();

        // Connect A
        bool connectedA = await clientA.ConnectAsync(relayPort, $"{roomId}:{tokenA}", TimeSpan.FromSeconds(5));
        Assert.True(connectedA, "Client A failed to connect to relay");

        // Connect B
        bool connectedB = await clientB.ConnectAsync(relayPort, $"{roomId}:{tokenB}", TimeSpan.FromSeconds(5));
        Assert.True(connectedB, "Client B failed to connect to relay");

        // Prepare received payload listeners
        var aReceivedFromB = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bReceivedFromA = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        clientA.Listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
        {
            aReceivedFromB.TrySetResult(reader.GetRemainingBytes());
        };

        clientB.Listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
        {
            bReceivedFromA.TrySetResult(reader.GetRemainingBytes());
        };

        // 1. A sends payload to B (opaque binary payload with random bytes)
        byte[] payloadFromA = new byte[256];
        RandomNumberGenerator.Fill(payloadFromA);
        clientA.Send(payloadFromA);

        var receivedByB = await bReceivedFromA.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(payloadFromA, receivedByB);

        // 2. B sends payload to A (JSON string payload)
        byte[] payloadFromB = Encoding.UTF8.GetBytes("{\"Type\":\"CardFlipped\",\"Position\":42,\"Player\":1}");
        clientB.Send(payloadFromB);

        var receivedByA = await aReceivedFromB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(payloadFromB, receivedByA);
    }

    [Fact]
    public async Task Relay_PayloadBeforeSecondPlayerConnects_IsDroppedGracefully()
    {
        await using var relay = CreateRelayService();
        int relayPort = relay.Port;

        var createResult = await relay.CreateRoomAsync("playerA", "Solo Room", null, "Animals", 1);
        string roomId = createResult.Value!.RoomId;
        string tokenA = createResult.Value.ConnectionToken;

        await using var clientA = new TestClient();
        bool connectedA = await clientA.ConnectAsync(relayPort, $"{roomId}:{tokenA}", TimeSpan.FromSeconds(5));
        Assert.True(connectedA);

        // Client A sends payload when B is not in the room yet
        byte[] payload = Encoding.UTF8.GetBytes("Hello to nobody");
        clientA.Send(payload);

        // Wait a bit to ensure server didn't crash or throw
        await Task.Delay(100);
        Assert.True(clientA.IsConnected);
    }

    [Fact]
    public async Task Relay_WhenPlayerADisconnects_PlayerBIsDisconnectedAndRoomIsCleanedUp()
    {
        await using var relay = CreateRelayService();
        int relayPort = relay.Port;

        var createResult = await relay.CreateRoomAsync("playerA", "Disconnect Room", null, "Animals", 1);
        string roomId = createResult.Value!.RoomId;
        string tokenA = createResult.Value.ConnectionToken;

        var joinResult = await relay.JoinRoomAsync("playerB", roomId, null);
        string tokenB = joinResult.Value!.ConnectionToken;

        await using var clientA = new TestClient();
        await using var clientB = new TestClient();

        await clientA.ConnectAsync(relayPort, $"{roomId}:{tokenA}", TimeSpan.FromSeconds(5));
        await clientB.ConnectAsync(relayPort, $"{roomId}:{tokenB}", TimeSpan.FromSeconds(5));

        var bDisconnectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        clientB.Listener.PeerDisconnectedEvent += (_, _) => bDisconnectedTcs.TrySetResult(true);

        // Player A disconnects
        clientA.Peer?.Disconnect();

        // Player B should receive disconnect within 5s
        bool bDisconnected = await bDisconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(bDisconnected);

        // Room should no longer be listed
        var list = await relay.ListRoomsAsync();
        Assert.DoesNotContain(list.Value!, r => r.RoomId == roomId);
    }

    [Fact]
    public async Task Relay_WhenPlayerBDisconnects_PlayerAIsDisconnectedAndRoomIsCleanedUp()
    {
        await using var relay = CreateRelayService();
        int relayPort = relay.Port;

        var createResult = await relay.CreateRoomAsync("playerA", "Disconnect Room B", null, "Animals", 1);
        string roomId = createResult.Value!.RoomId;
        string tokenA = createResult.Value.ConnectionToken;

        var joinResult = await relay.JoinRoomAsync("playerB", roomId, null);
        string tokenB = joinResult.Value!.ConnectionToken;

        await using var clientA = new TestClient();
        await using var clientB = new TestClient();

        await clientA.ConnectAsync(relayPort, $"{roomId}:{tokenA}", TimeSpan.FromSeconds(5));
        await clientB.ConnectAsync(relayPort, $"{roomId}:{tokenB}", TimeSpan.FromSeconds(5));

        var aDisconnectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        clientA.Listener.PeerDisconnectedEvent += (_, _) => aDisconnectedTcs.TrySetResult(true);

        // Player B disconnects
        clientB.Peer?.Disconnect();

        // Player A should receive disconnect
        bool aDisconnected = await aDisconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(aDisconnected);

        // Room should no longer be listed
        var list = await relay.ListRoomsAsync();
        Assert.DoesNotContain(list.Value!, r => r.RoomId == roomId);
    }
}

