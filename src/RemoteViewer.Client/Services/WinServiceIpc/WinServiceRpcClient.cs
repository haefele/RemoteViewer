using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace RemoteViewer.Client.Services.WinServiceIpc;

public sealed class WinServiceRpcClient : IAsyncDisposable
{
    private const int ReconnectIntervalMs = 5000;
    private const string PipeName = "RemoteViewer.WinService";

    private readonly ILogger<WinServiceRpcClient> _logger;
    private readonly Timer _reconnectTimer;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private readonly HashSet<string> _authenticatedConnectionIds = new(StringComparer.Ordinal);
    private readonly Lock _authLock = new();

    private NamedPipeClientStream? _pipeClient;
    private JsonRpc? _jsonRpc;
    private IWinServiceRpc? _proxy;
    private bool _disposed;

    public bool IsConnected
    {
        get;
        private set
        {
            if (field == value)
                return;

            field = value;
            this._connectionStatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public WinServiceRpcClient(ILogger<WinServiceRpcClient> logger)
    {
        this._logger = logger;

        // Start background reconnection timer
        this._reconnectTimer = new Timer(this.TryReconnect, null, 0, ReconnectIntervalMs);
    }

    public IWinServiceRpc? Proxy => this._proxy;

    public bool IsAuthenticatedFor(string connectionId)
    {
        using (this._authLock.EnterScope())
        {
            return this._authenticatedConnectionIds.Contains(connectionId);
        }
    }

    public async Task<bool> AuthenticateForConnectionAsync(string token, string connectionId, CancellationToken ct)
    {
        if (this.IsConnected is false || this._proxy is null)
        {
            this._logger.LogWarning("Cannot authenticate WinService - IPC not connected");
            return false;
        }

        var result = await this._proxy.Authenticate(token, ct);
        if (result.Success)
        {
            using (this._authLock.EnterScope())
            {
                this._authenticatedConnectionIds.Add(connectionId);
            }
            this._logger.LogInformation("WinService IPC authenticated for connection: {ConnectionId}", connectionId);
            return true;
        }

        this._logger.LogWarning("WinService IPC authentication failed: {Error}", result.Error);
        return false;
    }

    private EventHandler? _connectionStatusChanged;
    public event EventHandler? ConnectionStatusChanged
    {
        add
        {
            this._connectionStatusChanged += value;
            value?.Invoke(this, EventArgs.Empty);
        }
        remove => this._connectionStatusChanged -= value;
    }

    private async void TryReconnect(object? state)
    {
        if (this._disposed || this.IsConnected)
            return;

        if (!await this._connectionLock.WaitAsync(0))
            return;

        try
        {
            await this.ConnectAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "Failed to connect to WinService on pipe {PipeName}", PipeName);
        }
        finally
        {
            this._connectionLock.Release();
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        if (this.IsConnected)
            return;

        await this.CleanupConnectionAsync();

        this._pipeClient = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));

        await this._pipeClient.ConnectAsync(cts.Token);

        var formatter = new NerdbankMessagePackFormatter
        {
            TypeShapeProvider = WinServiceIpcWitness.GeneratedTypeShapeProvider
        };
        var handler = new LengthHeaderMessageHandler(this._pipeClient.UsePipe(cancellationToken: ct), formatter);
        this._jsonRpc = new JsonRpc(handler);

        this._jsonRpc.Disconnected += (sender, args) =>
        {
            this._logger.LogInformation("Disconnected from WinService: {Reason}", args.Reason);
            _ = this.CleanupConnectionAsync();
        };

        this._proxy = this._jsonRpc.Attach<IWinServiceRpc>();
        this._jsonRpc.StartListening();
        this.IsConnected = true;

        this._logger.LogInformation("Connected to WinService on pipe {PipeName}", PipeName);
    }

    private async Task CleanupConnectionAsync()
    {
        this._jsonRpc?.Dispose();
        this._jsonRpc = null;

        this._proxy = null;

        if (this._pipeClient is not null)
        {
            await this._pipeClient.DisposeAsync();
            this._pipeClient = null;
        }

        using (this._authLock.EnterScope())
        {
            this._authenticatedConnectionIds.Clear();
        }

        this.IsConnected = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
            return;

        this._disposed = true;
        await this._reconnectTimer.DisposeAsync();
        await this.CleanupConnectionAsync();
        this._connectionLock.Dispose();
    }
}
