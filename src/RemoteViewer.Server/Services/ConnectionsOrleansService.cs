using Orleans;
using RemoteViewer.Server.Grains;
using RemoteViewer.Shared;

namespace RemoteViewer.Server.Services;

public sealed class ConnectionsOrleansService(IGrainFactory grainFactory) : IConnectionsService
{
    public async Task Register(string signalrConnectionId, string? displayName)
    {
        var clientGrain = grainFactory.GetGrain<IClientGrain>(signalrConnectionId);
        await clientGrain.InitializeAsync(displayName);
    }

    public async Task Unregister(string signalrConnectionId)
    {
        var clientGrain = grainFactory.GetGrain<IClientGrain>(signalrConnectionId);
        await clientGrain.DeactivateAsync();
    }

    public async Task GenerateNewPassword(string signalrConnectionId)
    {
        var clientGrain = grainFactory.GetGrain<IClientGrain>(signalrConnectionId);
        await clientGrain.GenerateNewPasswordAsync();
    }

    public async Task SetDisplayName(string signalrConnectionId, string displayName)
    {
        var clientGrain = grainFactory.GetGrain<IClientGrain>(signalrConnectionId);
        await clientGrain.SetDisplayNameAsync(displayName);
    }

    public async Task<TryConnectError?> TryConnectTo(string signalrConnectionId, string username, string password)
    {
        var viewerGrain = grainFactory.GetGrain<IClientGrain>(signalrConnectionId);

        if (!await viewerGrain.IsInitializedAsync())
        {
            return TryConnectError.ViewerNotFound;
        }

        var normalizedUsername = username.Replace(" ", "");

        var usernameGrain = grainFactory.GetGrain<IUsernameGrain>(normalizedUsername);
        var presenterSignalrId = await usernameGrain.GetSignalrConnectionIdAsync();

        if (presenterSignalrId is null)
        {
            return TryConnectError.IncorrectUsernameOrPassword;
        }

        var presenterGrain = grainFactory.GetGrain<IClientGrain>(presenterSignalrId);

        if (!await presenterGrain.ValidatePasswordAsync(password))
        {
            return TryConnectError.IncorrectUsernameOrPassword;
        }

        if (signalrConnectionId == presenterSignalrId)
        {
            return TryConnectError.CannotConnectToYourself;
        }

        var connectionId = await presenterGrain.GetOrCreateConnectionAsync();
        var connectionGrain = grainFactory.GetGrain<IConnectionGrain>(connectionId);
        await connectionGrain.AddViewerAsync(signalrConnectionId);

        return null;
    }

    public async Task DisconnectFromConnection(string signalrConnectionId, string connectionId)
    {
        var clientGrain = grainFactory.GetGrain<IClientGrain>(signalrConnectionId);
        await clientGrain.LeaveConnectionAsync(connectionId);
    }

    public async Task SetConnectionProperties(string signalrConnectionId, string connectionId, ConnectionProperties properties)
    {
        var connectionGrain = grainFactory.GetGrain<IConnectionGrain>(connectionId);
        await connectionGrain.UpdatePropertiesAsync(signalrConnectionId, properties);
    }

    public async Task SendMessage(string signalrConnectionId, string connectionId, string messageType, byte[] data, MessageDestination destination, IReadOnlyList<string>? targetClientIds = null)
    {
        var connectionGrain = grainFactory.GetGrain<IConnectionGrain>(connectionId);
        await connectionGrain.SendMessageAsync(signalrConnectionId, messageType, data, destination, targetClientIds);
    }

    public async Task<bool> IsPresenterOfConnection(string signalrConnectionId, string connectionId)
    {
        var connectionGrain = grainFactory.GetGrain<IConnectionGrain>(connectionId);
        return await connectionGrain.IsPresenterAsync(signalrConnectionId);
    }
}
