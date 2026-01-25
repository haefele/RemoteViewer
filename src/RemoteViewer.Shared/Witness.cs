using PolyType;
using RemoteViewer.Shared.Protocol;

namespace RemoteViewer.Shared;

// Primitives used in hub methods
[GenerateShapeFor<string>]
[GenerateShapeFor<bool>]
[GenerateShapeFor<byte[]>]
[GenerateShapeFor<object>]
// Hub types
[GenerateShapeFor<ConnectionInfo>]
[GenerateShapeFor<ConnectionProperties>]
[GenerateShapeFor<DisplayInfo>]
[GenerateShapeFor<List<DisplayInfo>>]
[GenerateShapeFor<TryConnectError>]
[GenerateShapeFor<TryConnectError?>]
[GenerateShapeFor<MessageDestination>]
[GenerateShapeFor<List<string>>]
// Auth models
[GenerateShapeFor<ClientRegistrationResponse>]
[GenerateShapeFor<ClientAuthChallenge>]
[GenerateShapeFor<ClientAuthResponse>]
[GenerateShapeFor<ClientAuthResult>]
// Auth API models
[GenerateShapeFor<ClientRegistrationRequest>]
[GenerateShapeFor<ClientAuthNonceRequest>]
[GenerateShapeFor<ClientAuthRequest>]
[GenerateShapeFor<ClientAuthTokenResponse>]
[GenerateShapeFor<IpcTokenRequest>]
[GenerateShapeFor<IpcTokenResponse>]
[GenerateShapeFor<IpcTokenValidateRequest>]
[GenerateShapeFor<IpcTokenValidateResponse>]
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
[GenerateShapeFor<ClipboardTextMessage>]
[GenerateShapeFor<ClipboardImageMessage>]
[GenerateShapeFor<ChatMessage>]
[GenerateShapeFor<TextInputMessage>]
public partial class Witness;
