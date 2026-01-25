using RemoteViewer.Server.Orleans.Grains;
using RemoteViewer.Shared;

namespace RemoteViewer.Server.Services;

public sealed class ConnectionsOrleansService(IGrainFactory grainFactory) : IConnectionsService
{
    public async Task Register(string signalrConnectionId, string clientGuid, string? displayName)
    {
        var clientGrain = grainFactory.GetGrain<IClientGrain>(signalrConnectionId);
        await clientGrain.Initialize(clientGuid, displayName);

        var mapGrain = grainFactory.GetGrain<IClientGuidMapGrain>(clientGuid);
        await mapGrain.SetSignalrConnectionIdAsync(signalrConnectionId);
    }

    public async Task Unregister(string signalrConnectionId)
    {
        var clientGrain = grainFactory.GetGrain<IClientGrain>(signalrConnectionId);
        var clientGuid = await clientGrain.Internal_GetClientGuid();
        await clientGrain.Deactivate();

        if (!string.IsNullOrWhiteSpace(clientGuid))
        {
            var mapGrain = grainFactory.GetGrain<IClientGuidMapGrain>(clientGuid);
            await mapGrain.ClearSignalrConnectionIdAsync(signalrConnectionId);
        }
    }

    public async Task<string?> GetSignalrConnectionIdAsync(string clientGuid)
    {
        var mapGrain = grainFactory.GetGrain<IClientGuidMapGrain>(clientGuid);
        return await mapGrain.GetSignalrConnectionIdAsync();
    }

    public async Task GenerateNewPassword(string signalrConnectionId)
    {
        var clientGrain = grainFactory.GetGrain<IClientGrain>(signalrConnectionId);
        await clientGrain.GenerateNewPassword();
    }

    public async Task SetDisplayName(string signalrConnectionId, string displayName)
    {
        var clientGrain = grainFactory.GetGrain<IClientGrain>(signalrConnectionId);
        await clientGrain.SetDisplayName(displayName);
    }

    public async Task<TryConnectError?> TryConnectTo(string signalrConnectionId, string username, string password)
    {
        var viewerGrain = grainFactory.GetGrain<IClientGrain>(signalrConnectionId);
        if (await viewerGrain.IsInitialized() is false)
        {
            return TryConnectError.ViewerNotFound;
        }

        var usernameGrain = grainFactory.GetGrain<IUsernameGrain>(username.Replace(" ", ""));
        var presenterSignalrId = await usernameGrain.GetSignalrConnectionIdAsync();
        if (presenterSignalrId is null)
        {
            return TryConnectError.IncorrectUsernameOrPassword;
        }

        if (signalrConnectionId == presenterSignalrId)
        {
            return TryConnectError.CannotConnectToYourself;
        }

        var presenterGrain = grainFactory.GetGrain<IClientGrain>(presenterSignalrId);
        var connectionGrain = await presenterGrain.ValidatePasswordAndStartPresenting(password);
        if (connectionGrain is null)
            return TryConnectError.IncorrectUsernameOrPassword;

        await viewerGrain.ViewerJoinConnection(connectionGrain);

        return null;
    }

    public async Task DisconnectFromConnection(string signalrConnectionId, string connectionId)
    {
        var connectionGrain = grainFactory.GetGrain<IConnectionGrain>(connectionId);
        var clientGrain = grainFactory.GetGrain<IClientGrain>(signalrConnectionId);
        await clientGrain.LeaveConnection(connectionGrain);
    }

    public async Task SetConnectionProperties(string signalrConnectionId, string connectionId, ConnectionProperties properties)
    {
        var connectionGrain = grainFactory.GetGrain<IConnectionGrain>(connectionId);
        await connectionGrain.UpdateProperties(signalrConnectionId, properties);
    }

    public async Task SendMessage(string signalrConnectionId, string connectionId, string messageType, byte[] data, MessageDestination destination, IReadOnlyList<string>? targetClientIds = null)
    {
        var connectionGrain = grainFactory.GetGrain<IConnectionGrain>(connectionId);
        await connectionGrain.SendMessage(signalrConnectionId, messageType, data, destination, targetClientIds);
    }

    public Task AckFrame(string signalrConnectionId, string connectionId)
    {
        var grain = grainFactory.GetGrain<IClientSendGrain>(signalrConnectionId);
        return grain.AckFrame(connectionId);
    }


    public async Task<bool> IsPresenterOfConnection(string signalrConnectionId, string connectionId)
    {
        var connectionGrain = grainFactory.GetGrain<IConnectionGrain>(connectionId);
        return await connectionGrain.IsPresenter(signalrConnectionId);
    }
}
