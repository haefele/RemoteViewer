using Microsoft.AspNetCore.Http.Connections;

namespace RemoteViewer.Client.Services.HubClient;

public class ConnectionHubClientOptions
{
#if DEBUG
    public string BaseUrl { get; set; } = "http://localhost:8080";
#else
    public string BaseUrl { get; set; } = "https://rdp.xemio.net";
#endif

    public Func<HttpMessageHandler>? HttpMessageHandlerFactory { get; set; }

    /// <summary>
    /// Configures which transports SignalR should use. Default is all transports.
    /// In tests, set to LongPolling to avoid WebSocket timeout delays.
    /// </summary>
    public HttpTransportType? Transports { get; set; }
}
