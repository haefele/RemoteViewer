using System.Buffers;

namespace RemoteViewer.Client.Common;

public static class SmartArrayPool
{
    public static ArrayPool<byte> Bytes { get; set; } = new NoPoolAtAll();

    private sealed class NoPoolAtAll : ArrayPool<byte>
    {
        public override byte[] Rent(int minimumLength)
        {
            return new byte[minimumLength];
        }
        public override void Return(byte[] array, bool clearArray = false)
        {
        }
    }
}
