using Microsoft.AspNetCore.Http.Connections;

namespace RemoteViewer.Client.Services.HubClient;

public class ConnectionHubClientOptions
{
#if DEBUG
    public string BaseUrl { get; set; } = "http://localhost:8080";
#else
    public string BaseUrl { get; set; } = "https://rdp.xemio.net";
#endif
}
