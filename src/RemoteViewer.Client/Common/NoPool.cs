using System.Buffers;

namespace RemoteViewer.Client.Common;

public sealed class NoPool : ArrayPool<byte>
{
    public override byte[] Rent(int minimumLength)
    {
        return new byte[minimumLength];
    }
    public override void Return(byte[] array, bool clearArray = false)
    {
    }
}
