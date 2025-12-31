using PolyType;
using RemoteViewer.Client.Services.Screenshot;

namespace RemoteViewer.Client.Services.WindowsIpc;

[GenerateShapeFor<DisplayDto>]
[GenerateShapeFor<DisplayDto[]>]
[GenerateShapeFor<SharedRegionInfo>]
[GenerateShapeFor<SharedRegionInfo[]>]
[GenerateShapeFor<MoveRegionDto>]
[GenerateShapeFor<MoveRegionDto[]>]
[GenerateShapeFor<SharedFrameResult>]
[GenerateShapeFor<GrabStatus>]
[GenerateShapeFor<string>]
[GenerateShapeFor<bool>]
[GenerateShapeFor<int>]
[GenerateShapeFor<float>]
[GenerateShapeFor<ushort>]
public partial class IpcWitness;
