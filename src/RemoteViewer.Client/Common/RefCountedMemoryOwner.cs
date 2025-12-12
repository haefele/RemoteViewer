using System.Buffers;

namespace RemoteViewer.Client.Common;

public sealed class RefCountedMemoryOwner<T> : IMemoryOwner<T>, IDisposable
{
    private readonly ArrayPool<T> _pool;
    private readonly T[] _array;

    private int _refCount = 1;
    private int _disposed;

    public static RefCountedMemoryOwner<T> Create(int length) => new RefCountedMemoryOwner<T>(ArrayPool<T>.Shared, length);
    private RefCountedMemoryOwner(ArrayPool<T> pool, int length)
    {
        this._pool = pool;
        this._array = this._pool.Rent(length);
        this.Length = length;
    }

    public int Length { get; private set; }

    public Memory<T> Memory
    {
        get
        {
            ObjectDisposedException.ThrowIf(this._disposed != 0, nameof(RefCountedMemoryOwner<T>));
            return new Memory<T>(this._array, 0, this.Length);
        }
    }

    public Span<T> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(this._disposed != 0, nameof(RefCountedMemoryOwner<T>));
            return new Span<T>(this._array, 0, this.Length);
        }
    }

    public RefCountedMemoryOwner<T> AddRef()
    {
        ObjectDisposedException.ThrowIf(this._disposed != 0, nameof(RefCountedMemoryOwner<T>));

        Interlocked.Increment(ref this._refCount);
        return this;
    }

    public void SetLength(int newLength)
    {
        ObjectDisposedException.ThrowIf(this._disposed != 0, nameof(RefCountedMemoryOwner<T>));

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
