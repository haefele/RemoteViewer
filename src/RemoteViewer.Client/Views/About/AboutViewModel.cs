using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RemoteViewer.Client.Views.About;

public partial class AboutViewModel : ObservableObject
{
    public string ApplicationName => "Remote Viewer";

    public string Version => ThisAssembly.AssemblyInformationalVersion;

    public string Copyright { get; }

    public IReadOnlyList<ThirdPartyLicense> Licenses { get; } =
    [
        new("Avalonia", "https://avaloniaui.net/", "MIT", "https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md"),
        new("Material.Icons.Avalonia", "https://github.com/SKProCH/Material.Icons.Avalonia", "MIT", "https://github.com/SKProCH/Material.Icons.Avalonia/blob/master/LICENSE"),
        new("CommunityToolkit.Mvvm", "https://github.com/CommunityToolkit/dotnet", "MIT", "https://github.com/CommunityToolkit/dotnet/blob/main/License.md"),
        new("Microsoft.AspNetCore.SignalR.Client", "https://github.com/dotnet/aspnetcore", "MIT", "https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt"),
        new("Microsoft.Extensions.DependencyInjection", "https://github.com/dotnet/runtime", "MIT", "https://github.com/dotnet/runtime/blob/main/LICENSE.TXT"),
        new("Microsoft.Extensions.Hosting", "https://github.com/dotnet/runtime", "MIT", "https://github.com/dotnet/runtime/blob/main/LICENSE.TXT"),
        new("Nerdbank.MessagePack", "https://github.com/AArnott/Nerdbank.MessagePack", "MIT", "https://github.com/AArnott/Nerdbank.MessagePack/blob/main/LICENSE"),
        new("Nerdbank.Streams", "https://github.com/AArnott/Nerdbank.Streams", "MIT", "https://github.com/AArnott/Nerdbank.Streams/blob/main/LICENSE"),
        new("StreamJsonRpc", "https://github.com/microsoft/vs-streamjsonrpc", "MIT", "https://github.com/microsoft/vs-streamjsonrpc/blob/main/LICENSE"),
        new("Serilog", "https://serilog.net/", "Apache-2.0", "https://github.com/serilog/serilog/blob/dev/LICENSE"),
        new("Quamotion.TurboJpegWrapper", "https://github.com/quamotion/AS.TurboJpegWrapper", "MIT", "https://github.com/quamotion/AS.TurboJpegWrapper/blob/master/LICENSE"),
        new("ZiggyCreatures.FusionCache", "https://github.com/ZiggyCreatures/FusionCache", "MIT", "https://github.com/ZiggyCreatures/FusionCache/blob/main/LICENSE"),
        new("PolyType", "https://github.com/eiriktsarpalis/PolyType", "MIT", "https://github.com/eiriktsarpalis/PolyType/blob/main/LICENSE"),
    ];

    public AboutViewModel(TimeProvider timeProvider)
    {
        this.Copyright = $"Copyright {timeProvider.GetUtcNow().Year} RemoteViewer Contributors";
    }

    [RelayCommand]
    private void Close()
    {
        this.CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CloseRequested;
}

public sealed record ThirdPartyLicense(
    string Name,
    string Url,
    string LicenseName,
    string LicenseUrl);
