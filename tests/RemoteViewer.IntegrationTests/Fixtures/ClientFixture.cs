using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using RemoteViewer.Client;
using RemoteViewer.Client.Services;
using RemoteViewer.Client.Services.Clipboard;
using RemoteViewer.Client.Services.Dialogs;
using RemoteViewer.Client.Services.Dispatching;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.LocalInputMonitor;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.SessionRecorderIpc;
using RemoteViewer.Client.Services.WinServiceIpc;
using RemoteViewer.Client.Views.Presenter;
using RemoteViewer.IntegrationTests.Mocks;
using RemoteViewer.Shared;
using RemoteViewer.Shared.Protocol;
using TUnit.Core;

namespace RemoteViewer.IntegrationTests.Fixtures;

public class ClientFixture : IAsyncDisposable
{
    private static readonly DisplayInfo s_fakeDisplay = new(
        Id: "DISPLAY1",
        FriendlyName: "Test Display",
        IsPrimary: true,
        Left: 0,
        Top: 0,
        Right: 1920,
        Bottom: 1080);

    private readonly IServiceProvider _serviceProvider;

    public ConnectionHubClient HubClient { get; }
    public Connection? CurrentConnection => this.HubClient.Connections.FirstOrDefault();

    // NSubstitute mocks exposed for test configuration/verification
    public IInputInjectionService InputInjectionService { get; }
    public IDialogService DialogService { get; }
    public IClipboardService ClipboardService { get; }
    public IDisplayService DisplayService { get; }
    public IScreenshotService ScreenshotService { get; }
    public ILocalInputMonitorService LocalInputMonitorService { get; }
    public FakeTimeProvider TimeProvider { get; }

    public ClientFixture(ServerFixture serverFixture, string? displayName = null)
    {
        // Create NSubstitute mocks with default behaviors
        this.InputInjectionService = CreateInputInjectionServiceMock();
        this.DialogService = CreateDialogServiceMock();
        this.ClipboardService = CreateClipboardServiceMock();
        this.DisplayService = CreateDisplayServiceMock();
        this.ScreenshotService = CreateScreenshotServiceMock();
        this.LocalInputMonitorService = CreateLocalInputMonitorServiceMock();
        this.TimeProvider = new FakeTimeProvider();

        var services = new ServiceCollection();
        services.AddRemoteViewerServices(ApplicationMode.Desktop, app: null);

        // Configure hub client to use test server
        services.Configure<ConnectionHubClientOptions>(options =>
        {
            options.BaseUrl = serverFixture.ClientOptions.BaseAddress.ToString().TrimEnd('/');
        });

        // Replace Avalonia-specific services with test implementations
        // Using Replace() to explicitly remove existing registrations (not just "last wins")
        services.Replace(ServiceDescriptor.Singleton<IDispatcher, TestDispatcher>());
        services.Replace(ServiceDescriptor.Singleton(this.InputInjectionService));
        services.Replace(ServiceDescriptor.Singleton(this.DialogService));
        services.Replace(ServiceDescriptor.Singleton(this.ClipboardService));
        services.Replace(ServiceDescriptor.Singleton(this.DisplayService));
        services.Replace(ServiceDescriptor.Singleton(this.ScreenshotService));
        services.Replace(ServiceDescriptor.Singleton(this.LocalInputMonitorService));
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(this.TimeProvider));

        // Replace RPC client instances with ones using null loggers
        // (they'll fail to connect to non-existent pipes, which is fine for tests)
        var nullSessionLogger = Substitute.For<ILogger<SessionRecorderRpcClient>>();
        var nullWinServiceLogger = Substitute.For<ILogger<WinServiceRpcClient>>();
        services.Replace(ServiceDescriptor.Singleton(new SessionRecorderRpcClient(nullSessionLogger)));
        services.Replace(ServiceDescriptor.Singleton(new WinServiceRpcClient(nullWinServiceLogger)));

        this._serviceProvider = services.BuildServiceProvider();
        this.HubClient = this._serviceProvider.GetRequiredService<ConnectionHubClient>();

        if (displayName != null)
            _ = this.HubClient.SetDisplayName(displayName);
    }

    private static IInputInjectionService CreateInputInjectionServiceMock()
    {
        var mock = Substitute.For<IInputInjectionService>();
        mock.InjectMouseMove(Arg.Any<DisplayInfo>(), Arg.Any<float>(), Arg.Any<float>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        mock.InjectMouseButton(Arg.Any<DisplayInfo>(), Arg.Any<MouseButton>(), Arg.Any<bool>(), Arg.Any<float>(), Arg.Any<float>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        mock.InjectMouseWheel(Arg.Any<DisplayInfo>(), Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        mock.InjectKey(Arg.Any<ushort>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        mock.ReleaseAllModifiers(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static IDialogService CreateDialogServiceMock()
    {
        var mock = Substitute.For<IDialogService>();
        mock.ShowFileTransferConfirmationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(false));
        mock.ShowViewerSelectionAsync(Arg.Any<IReadOnlyList<PresenterViewerDisplay>>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult<IReadOnlyList<string>?>(null));
        return mock;
    }

    private static IClipboardService CreateClipboardServiceMock()
    {
        var mock = Substitute.For<IClipboardService>();
        mock.TryGetTextAsync().Returns(Task.FromResult<string?>(null));
        mock.GetDataFormatsAsync().Returns(Task.FromResult<IReadOnlyList<Avalonia.Input.DataFormat>>([]));
        mock.TryGetBitmapAsync().Returns(Task.FromResult<Avalonia.Media.Imaging.Bitmap?>(null));
        mock.SetTextAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        mock.SetDataAsync(Arg.Any<Avalonia.Input.IAsyncDataTransfer?>()).Returns(Task.CompletedTask);
        return mock;
    }

    private static IDisplayService CreateDisplayServiceMock()
    {
        var mock = Substitute.For<IDisplayService>();
        mock.GetDisplays(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ImmutableList.Create(s_fakeDisplay)));
        return mock;
    }

    private static IScreenshotService CreateScreenshotServiceMock()
    {
        var mock = Substitute.For<IScreenshotService>();
        mock.CaptureDisplay(Arg.Any<DisplayInfo>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GrabResult(GrabStatus.NoChanges, null, null, null)));
        mock.ForceKeyframe(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static ILocalInputMonitorService CreateLocalInputMonitorServiceMock()
    {
        var mock = Substitute.For<ILocalInputMonitorService>();
        mock.ShouldSuppressViewerInput().Returns(false);
        return mock;
    }

    public async Task<(string Username, string Password)> WaitForCredentialsAsync(TimeSpan? timeout = null)
    {
        if (this.HubClient.Username != null)
        {
            // Strip spaces - usernames are formatted with spaces for display but ConnectTo expects them stripped
            return (this.HubClient.Username.Replace(" ", ""), this.HubClient.Password!);
        }

        var tcs = new TaskCompletionSource<(string, string)>();
        this.HubClient.CredentialsAssigned += (s, e) => tcs.TrySetResult((e.Username, e.Password));

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        cts.Token.Register(() => tcs.TrySetCanceled());
        var (username, password) = await tcs.Task;

        return (username.Replace(" ", ""), password);
    }

    public async Task<Connection> WaitForConnectionAsync(TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<Connection>();
        this.HubClient.ConnectionStarted += (s, e) => tcs.TrySetResult(e.Connection);

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        cts.Token.Register(() => tcs.TrySetCanceled());
        return await tcs.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await this.HubClient.DisposeAsync();
        if (this._serviceProvider is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }
}
