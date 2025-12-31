using PolyType;
using RemoteViewer.Client.Services.Screenshot;

namespace RemoteViewer.Client.Services.WindowsIpc;

[GenerateShapeFor<DisplayDto>]
[GenerateShapeFor<DisplayDto[]>]
[GenerateShapeFor<GrabResultDto>]
[GenerateShapeFor<DirtyRegionDto>]
[GenerateShapeFor<DirtyRegionDto[]>]
[GenerateShapeFor<MoveRegionDto>]
[GenerateShapeFor<MoveRegionDto[]>]
[GenerateShapeFor<GrabStatus>]
[GenerateShapeFor<ReadOnlyMemory<byte>>]
[GenerateShapeFor<string>]
[GenerateShapeFor<bool>]
[GenerateShapeFor<int>]
[GenerateShapeFor<float>]
[GenerateShapeFor<ushort>]
public partial class IpcWitness;
