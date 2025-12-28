using System.Buffers;

namespace RemoteViewer.Client.Common;

public static class SmartArrayPool
{
    public static ArrayPool<byte> Bytes { get; } = new NoPool();
}
