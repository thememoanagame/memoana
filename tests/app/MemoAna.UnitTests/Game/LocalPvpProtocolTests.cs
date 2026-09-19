using System.Text.Json;
using System.Text;
using MemoAna.Game.Models;
using MemoAna.Game.Protocol;
using Xunit;

namespace MemoAna.UnitTests.Game;

public sealed class LocalPvpProtocolTests
{
    [Fact]
    public void SerializeAndDeserializePreservesMessageContract()
    {
        byte[] bytes = LocalPvpProtocol.Serialize(
            LocalPvpMessageType.CardFlip,
            "room-1",
            7,
            new LocalPvpCardFlip(4, 3, "client"));

        bool parsed = LocalPvpProtocol.TryDeserialize(bytes, "room-1", out LocalPvpMessage? message);

        Assert.True(parsed);
        Assert.NotNull(message);
        Assert.Equal(7, message.Sequence);
        Assert.Equal(LocalPvpMessageType.CardFlip, message.MessageType);
        Assert.Equal(new LocalPvpCardFlip(4, 3, "client"), message.Deserialize<LocalPvpCardFlip>(new(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void DeserializeRejectsMessagesForAnotherRoom()
    {
        byte[] bytes = LocalPvpProtocol.Serialize(
            LocalPvpMessageType.GameReady,
            "room-1",
            1,
            new { });

        bool parsed = LocalPvpProtocol.TryDeserialize(bytes, "room-2", out _);

        Assert.False(parsed);
    }

    [Fact]
    public void DeserializeRejectsMalformedAndOversizedMessages()
    {
        Assert.False(LocalPvpProtocol.TryDeserialize(Encoding.UTF8.GetBytes("{"), "room-1", out _));
        Assert.False(LocalPvpProtocol.TryDeserialize(
            new byte[LocalPvpProtocol.MaxMessageBytes + 1],
            "room-1",
            out _));
    }

    [Fact]
    public void SerializeAssignsDistinctMessageIdentifiers()
    {
        byte[] first = LocalPvpProtocol.Serialize(LocalPvpMessageType.GameReady, "room-1", 1, new { });
        byte[] second = LocalPvpProtocol.Serialize(LocalPvpMessageType.GameReady, "room-1", 2, new { });

        Assert.True(LocalPvpProtocol.TryDeserialize(first, "room-1", out LocalPvpMessage? firstMessage));
        Assert.True(LocalPvpProtocol.TryDeserialize(second, "room-1", out LocalPvpMessage? secondMessage));
        Assert.NotEqual(firstMessage!.MessageId, secondMessage!.MessageId);
    }
}
