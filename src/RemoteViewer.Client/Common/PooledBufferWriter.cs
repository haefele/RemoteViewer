using System.Buffers;

namespace RemoteViewer.Client.Common;

public sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _written;
    private bool _disposed;

    private PooledBufferWriter(int initialCapacity)
    {
        this._buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        this._written = 0;
    }

    public static PooledBufferWriter Rent(int initialCapacity = 256) => new(initialCapacity);

    public ReadOnlyMemory<byte> WrittenMemory
    {
        get
        {
            ObjectDisposedException.ThrowIf(this._disposed, this);
            return new ReadOnlyMemory<byte>(this._buffer, 0, this._written);
        }
    }

    public int WrittenCount => this._written;

    public void Advance(int count)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (this._written + count > this._buffer.Length)
            throw new InvalidOperationException("Cannot advance past the end of the buffer.");

        this._written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        this.EnsureCapacity(sizeHint);
        return this._buffer.AsMemory(this._written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        this.EnsureCapacity(sizeHint);
        return this._buffer.AsSpan(this._written);
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        this._written = 0;
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;
        ArrayPool<byte>.Shared.Return(this._buffer);
        this._buffer = null!;
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint <= 0)
            sizeHint = 1;

        var available = this._buffer.Length - this._written;
        if (available >= sizeHint)
            return;

        var newSize = Math.Max(this._buffer.Length * 2, this._written + sizeHint);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        this._buffer.AsSpan(0, this._written).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(this._buffer);
        this._buffer = newBuffer;
    }
}
