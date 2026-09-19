using System.Text.Json;
using MemoAna.Game.Models;

namespace MemoAna.Game.Protocol;

/// <summary>
/// Encodes and validates the transport-independent Local PVP wire contract.
/// </summary>
public static class LocalPvpProtocol
{
    public const string ProtocolName = "MemoAnaPvp";
    public const int Version = 2;
    public const int MaxMessageBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates a compact message envelope with a unique identifier and sequence.
    /// </summary>
    public static byte[] Serialize(
        LocalPvpMessageType type,
        string roomId,
        long sequence,
        object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomId);
        ArgumentNullException.ThrowIfNull(payload);

        LocalPvpWireMessage message = new(
            ProtocolName,
            Version,
            Guid.CreateVersion7().ToString("N"),
            sequence,
            type,
            roomId,
            JsonSerializer.SerializeToElement(payload, JsonOptions));
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (bytes.Length > MaxMessageBytes)
            throw new InvalidDataException("The Local PVP message exceeds the maximum size.");
        return bytes;
    }

    /// <summary>
    /// Validates and parses a message received from a peer.
    /// </summary>
    public static bool TryDeserialize(
        ReadOnlySpan<byte> bytes,
        string expectedRoomId,
        out LocalPvpMessage message)
    {
        message = default!;
        if (bytes.IsEmpty || bytes.Length > MaxMessageBytes || string.IsNullOrWhiteSpace(expectedRoomId))
            return false;

        try
        {
            LocalPvpWireMessage? wire = JsonSerializer.Deserialize<LocalPvpWireMessage>(bytes, JsonOptions);
            if (wire is null ||
                !string.Equals(wire.Protocol, ProtocolName, StringComparison.Ordinal) ||
                wire.Version != Version ||
                !string.Equals(wire.RoomId, expectedRoomId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(wire.MessageId) ||
                wire.Sequence < 0)
                return false;

            message = new(wire.MessageId, wire.Sequence, wire.Type, wire.Payload);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed record LocalPvpWireMessage(
        string Protocol,
        int Version,
        string MessageId,
        long Sequence,
        LocalPvpMessageType Type,
        string RoomId,
        JsonElement Payload);
}

/// <summary>
/// A validated Local PVP message without transport-specific types.
/// </summary>
public sealed record LocalPvpMessage(
    string MessageId,
    long Sequence,
    LocalPvpMessageType MessageType,
    JsonElement Payload)
{
    public T Deserialize<T>(JsonSerializerOptions options) =>
        Payload.Deserialize<T>(options)
        ?? throw new InvalidDataException($"Invalid Local PVP payload for {MessageType}.");
}
