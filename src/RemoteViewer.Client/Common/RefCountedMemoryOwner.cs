using System.Buffers;

namespace RemoteViewer.Client.Common;

public sealed class RefCountedMemoryOwner : IMemoryOwner<byte>, IDisposable
{
    private readonly ArrayPool<byte> _pool;
    private readonly byte[] _array;

    private int _refCount = 1;
    private int _disposed;

    public static RefCountedMemoryOwner Create(int length) => new(SmartArrayPool.Bytes, length);

    private RefCountedMemoryOwner(ArrayPool<byte> pool, int length)
    {
        this._pool = pool;
        this._array = this._pool.Rent(length);
        this.Length = length;
    }

    public int Length { get; private set; }

    public Memory<byte> Memory
    {
        get
        {
            ObjectDisposedException.ThrowIf(this._disposed != 0, nameof(RefCountedMemoryOwner));
            return new Memory<byte>(this._array, 0, this.Length);
        }
    }

    public Span<byte> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(this._disposed != 0, nameof(RefCountedMemoryOwner));
            return new Span<byte>(this._array, 0, this.Length);
        }
    }

    public RefCountedMemoryOwner AddRef()
    {
        ObjectDisposedException.ThrowIf(this._disposed != 0, nameof(RefCountedMemoryOwner));

        Interlocked.Increment(ref this._refCount);
        return this;
    }

    public void SetLength(int newLength)
    {
        ObjectDisposedException.ThrowIf(this._disposed != 0, nameof(RefCountedMemoryOwner));

        if (newLength < 0 || newLength > this._array.Length)
            throw new ArgumentOutOfRangeException(nameof(newLength));

        this.Length = newLength;
    }

    public void Dispose()
    {
        if (Interlocked.Decrement(ref this._refCount) == 0)
        {
            if (Interlocked.Exchange(ref this._disposed, 1) == 0)
            {
                this._pool.Return(this._array);
            }
        }
    }
}
