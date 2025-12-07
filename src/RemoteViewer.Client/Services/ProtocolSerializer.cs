using Nerdbank.MessagePack;
using PolyType;

namespace RemoteViewer.Client.Services;

/// <summary>
/// Helper for serializing/deserializing protocol messages using MessagePack.
/// Uses the same serializer configuration as SignalR for compatibility.
/// </summary>
public static class ProtocolSerializer
{
    private static readonly MessagePackSerializer s_serializer = new();

    public static byte[] Serialize<T>(T message) where T : IShapeable<T>
    {
        return s_serializer.Serialize(message);
    }

    public static T Deserialize<T>(byte[] data) where T : IShapeable<T>
    {
        return s_serializer.Deserialize<T>(data)!;
    }
}
