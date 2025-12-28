using System.Buffers;

namespace RemoteViewer.Client.Common;

public sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private readonly ArrayPool<byte> _pool;

    private byte[] _buffer;
    private int _written;
    private bool _disposed;

    private PooledBufferWriter(ArrayPool<byte> pool, int initialCapacity)
    {
        this._pool = pool;
        this._buffer = this._pool.Rent(initialCapacity);
        this._written = 0;
    }

    public static PooledBufferWriter Rent(int initialCapacity = 256) => new(SmartArrayPool.Bytes, initialCapacity);

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
        this._pool.Return(this._buffer);
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
        var newBuffer = this._pool.Rent(newSize);
        this._buffer.AsSpan(0, this._written).CopyTo(newBuffer);
        this._pool.Return(this._buffer);
        this._buffer = newBuffer;
    }
}
