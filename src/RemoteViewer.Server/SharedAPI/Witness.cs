using PolyType;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Server.SharedAPI;

// Primitives used in hub methods
[GenerateShapeFor<string>]
[GenerateShapeFor<bool>]
[GenerateShapeFor<byte[]>]
[GenerateShapeFor<object>]
// Hub types
[GenerateShapeFor<ConnectionInfo>]
[GenerateShapeFor<TryConnectError>]
[GenerateShapeFor<TryConnectError?>]
[GenerateShapeFor<MessageDestination>]
[GenerateShapeFor<List<string>>]
// Protocol messages
[GenerateShapeFor<FrameMessage>]
[GenerateShapeFor<FrameRegion>]
[GenerateShapeFor<KeyMessage>]
[GenerateShapeFor<MouseMoveMessage>]
[GenerateShapeFor<MouseButtonMessage>]
[GenerateShapeFor<MouseWheelMessage>]
[GenerateShapeFor<FileSendRequestMessage>]
[GenerateShapeFor<FileSendResponseMessage>]
[GenerateShapeFor<FileChunkMessage>]
[GenerateShapeFor<FileCompleteMessage>]
[GenerateShapeFor<FileCancelMessage>]
[GenerateShapeFor<FileErrorMessage>]
public partial class Witness;
