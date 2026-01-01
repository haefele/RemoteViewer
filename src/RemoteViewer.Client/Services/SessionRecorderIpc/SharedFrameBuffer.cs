using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;

namespace RemoteViewer.Client.Services.SessionRecorderIpc;

/// <summary>
/// Per-display shared memory for zero-copy frame transfer between processes.
/// Sized exactly to display resolution (width × height × 4 bytes).
///
/// Security model:
/// 1. Random 128-bit token in name - unpredictable without RPC access
/// 2. Token only obtainable via secured named pipe (has PipeSecurity ACL)
/// 3. Session-local namespace - isolated per Windows session
///
/// Note: MemoryMappedFileSecurity was not ported to .NET Core+.
/// See: https://github.com/dotnet/runtime/issues/941
/// </summary>
public sealed class SharedFrameBuffer : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly unsafe byte* _basePointer;
    private readonly long _capacity;
    private int _disposed;

    public string Name { get; }
    public string Token { get; }
    public int Width { get; }
    public int Height { get; }

    private SharedFrameBuffer(string name, string token, int width, int height, long capacity, MemoryMappedFile mmf, MemoryMappedViewAccessor accessor)
    {
        this.Name = name;
        this.Token = token;
        this.Width = width;
        this.Height = height;
        this._capacity = capacity;
        this._mmf = mmf;
        this._accessor = accessor;

        unsafe
        {
            byte* ptr = null;
            this._accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            this._basePointer = ptr;
        }
    }

    private static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static long CalculateSize(int width, int height) => (long)width * height * 4;

    /// <summary>
    /// Creates a new shared memory buffer for a display (server-side).
    /// </summary>
    public static SharedFrameBuffer CreateServer(int width, int height)
    {
        var token = GenerateToken();
        var capacity = CalculateSize(width, height);
        var name = $"RemoteViewer.Frame.{token}";

        var mmf = MemoryMappedFile.CreateOrOpen(name, capacity, MemoryMappedFileAccess.ReadWrite);
        var accessor = mmf.CreateViewAccessor(0, capacity, MemoryMappedFileAccess.ReadWrite);
        return new SharedFrameBuffer(name, token, width, height, capacity, mmf, accessor);
    }

    /// <summary>
    /// Opens an existing shared memory buffer using info received via RPC (client-side).
    /// </summary>
    public static SharedFrameBuffer OpenClient(string token, int width, int height)
    {
        var capacity = CalculateSize(width, height);
        var name = $"RemoteViewer.Frame.{token}";

        var mmf = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
        var accessor = mmf.CreateViewAccessor(0, capacity, MemoryMappedFileAccess.Read);
        return new SharedFrameBuffer(name, token, width, height, capacity, mmf, accessor);
    }

    /// <summary>
    /// Writes data to the buffer at offset 0 (server-side).
    /// </summary>
    public void Write(ReadOnlySpan<byte> data) => this.WriteAt(0, data);

    /// <summary>
    /// Writes data to the buffer at the specified offset (server-side).
    /// </summary>
    public void WriteAt(int offset, ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(this._disposed != 0, this);

        if (offset + data.Length > this._capacity)
            throw new ArgumentException($"Write exceeds capacity: offset={offset}, length={data.Length}, capacity={this._capacity}");

        unsafe
        {
            var destination = new Span<byte>(this._basePointer + offset, data.Length);
            data.CopyTo(destination);
        }
    }

    /// <summary>
    /// Reads data from the buffer at offset 0 (client-side).
    /// </summary>
    public void Read(int length, Span<byte> destination) => this.ReadAt(0, length, destination);

    /// <summary>
    /// Reads data from the buffer at the specified offset (client-side).
    /// </summary>
    public void ReadAt(int offset, int length, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(this._disposed != 0, this);

        if (offset + length > this._capacity)
            throw new ArgumentException($"Read exceeds capacity: offset={offset}, length={length}, capacity={this._capacity}");

        if (destination.Length < length)
            throw new ArgumentException($"Destination too small: {destination.Length} < {length}", nameof(destination));

        unsafe
        {
            var source = new ReadOnlySpan<byte>(this._basePointer + offset, length);
            source.CopyTo(destination);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) != 0)
            return;

        this._accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        this._accessor.Dispose();
        this._mmf.Dispose();
    }
}
