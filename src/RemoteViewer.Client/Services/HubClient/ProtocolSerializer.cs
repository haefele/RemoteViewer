using System.Buffers;
using Nerdbank.MessagePack;
using PolyType;
using RemoteViewer.Shared;

namespace RemoteViewer.Client.Services.HubClient;

/// <summary>
/// Helper for serializing/deserializing protocol messages using MessagePack.
/// Uses the same serializer configuration as SignalR for compatibility.
/// </summary>
public static class ProtocolSerializer
{
    private static readonly MessagePackSerializer s_serializer = new();
    private static readonly ITypeShapeProvider s_provider = Witness.GeneratedTypeShapeProvider;

    public static void Serialize<T>(IBufferWriter<byte> writer, T message)
    {
        var shape = s_provider.GetTypeShapeOrThrow<T>();
        s_serializer.Serialize(writer, message, shape);
    }

    public static T Deserialize<T>(byte[] data)
    {
        var shape = s_provider.GetTypeShapeOrThrow<T>();
        return s_serializer.Deserialize(data, shape) ?? throw new InvalidOperationException($"Failed to deserialize data to type {typeof(T).FullName}");
    }
}
