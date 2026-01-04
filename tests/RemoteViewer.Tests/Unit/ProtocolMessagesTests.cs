using RemoteViewer.Server.SharedAPI;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Tests.Unit;

public class ProtocolMessagesTests
{
    public class DisplayInfoTests
    {
        [Theory]
        [InlineData(0, 0, 1920, 1080, 1920, 1080)]
        [InlineData(0, 0, 2560, 1440, 2560, 1440)]
        [InlineData(-1920, 0, 0, 1080, 1920, 1080)]
        [InlineData(1920, 0, 3840, 1080, 1920, 1080)]
        public void DisplayInfoWidthAndHeightCalculatedCorrectly(
            int left, int top, int right, int bottom,
            int expectedWidth, int expectedHeight)
        {
            var displayInfo = new DisplayInfo("id", "Display", true, left, top, right, bottom);

            displayInfo.Width.Should().Be(expectedWidth);
            displayInfo.Height.Should().Be(expectedHeight);
        }

        [Fact]
        public void DisplayInfoRecordEqualityWorksCorrectly()
        {
            var display1 = new DisplayInfo("id1", "Display 1", true, 0, 0, 1920, 1080);
            var display2 = new DisplayInfo("id1", "Display 1", true, 0, 0, 1920, 1080);
            var display3 = new DisplayInfo("id2", "Display 2", false, 0, 0, 1920, 1080);

            display1.Should().Be(display2);
            display1.Should().NotBe(display3);
        }
    }

    public class ClientInfoTests
    {
        [Fact]
        public void ClientInfoPropertiesSetCorrectly()
        {
            var clientInfo = new ClientInfo("client-123", "John Doe");

            clientInfo.ClientId.Should().Be("client-123");
            clientInfo.DisplayName.Should().Be("John Doe");
        }

        [Fact]
        public void ClientInfoWithEmptyDisplayNameAllowsEmpty()
        {
            var clientInfo = new ClientInfo("client-123", "");

            clientInfo.DisplayName.Should().BeEmpty();
        }
    }

    public class ConnectionPropertiesTests
    {
        [Fact]
        public void ConnectionPropertiesDefaultValuesSetCorrectly()
        {
            var props = new ConnectionProperties(
                CanSendSecureAttentionSequence: false,
                InputBlockedViewerIds: [],
                AvailableDisplays: []
            );

            props.CanSendSecureAttentionSequence.Should().BeFalse();
            props.InputBlockedViewerIds.Should().BeEmpty();
            props.AvailableDisplays.Should().BeEmpty();
        }

        [Fact]
        public void ConnectionPropertiesWithValuesSetCorrectly()
        {
            var displays = new List<DisplayInfo>
            {
                new("d1", "Primary", true, 0, 0, 1920, 1080),
                new("d2", "Secondary", false, 1920, 0, 3840, 1080)
            };

            var blockedIds = new List<string> { "viewer-1", "viewer-2" };

            var props = new ConnectionProperties(
                CanSendSecureAttentionSequence: true,
                InputBlockedViewerIds: blockedIds,
                AvailableDisplays: displays
            );

            props.CanSendSecureAttentionSequence.Should().BeTrue();
            props.InputBlockedViewerIds.Should().HaveCount(2);
            props.AvailableDisplays.Should().HaveCount(2);
        }
    }

    public class ConnectionInfoTests
    {
        [Fact]
        public void ConnectionInfoAllPropertiesSetCorrectly()
        {
            var presenter = new ClientInfo("presenter-1", "Presenter");
            var viewers = new List<ClientInfo>
            {
                new("viewer-1", "Viewer 1"),
                new("viewer-2", "Viewer 2")
            };
            var props = new ConnectionProperties(false, [], []);

            var connectionInfo = new ConnectionInfo("conn-123", presenter, viewers, props);

            connectionInfo.ConnectionId.Should().Be("conn-123");
            connectionInfo.Presenter.Should().Be(presenter);
            connectionInfo.Viewers.Should().HaveCount(2);
            connectionInfo.Properties.Should().Be(props);
        }
    }

    public class MouseMessagesTests
    {
        [Fact]
        public void MouseMoveMessagePropertiesSetCorrectly()
        {
            var msg = new MouseMoveMessage(100.5f, 200.75f);

            msg.X.Should().Be(100.5f);
            msg.Y.Should().Be(200.75f);
        }

        [Fact]
        public void MouseButtonMessageAllButtonsValidValues()
        {
            var buttons = Enum.GetValues<MouseButton>();
            buttons.Should().Contain(MouseButton.Left);
            buttons.Should().Contain(MouseButton.Right);
            buttons.Should().Contain(MouseButton.Middle);
            buttons.Should().Contain(MouseButton.XButton1);
            buttons.Should().Contain(MouseButton.XButton2);
        }

        [Theory]
        [InlineData(MouseButton.Left)]
        [InlineData(MouseButton.Right)]
        [InlineData(MouseButton.Middle)]
        public void MouseButtonMessagePropertiesSetCorrectly(MouseButton button)
        {
            var msg = new MouseButtonMessage(button, 50.0f, 75.0f);

            msg.Button.Should().Be(button);
            msg.X.Should().Be(50.0f);
            msg.Y.Should().Be(75.0f);
        }

        [Fact]
        public void MouseWheelMessagePropertiesSetCorrectly()
        {
            var msg = new MouseWheelMessage(0f, 120f, 100f, 200f);

            msg.DeltaX.Should().Be(0f);
            msg.DeltaY.Should().Be(120f);
            msg.X.Should().Be(100f);
            msg.Y.Should().Be(200f);
        }

        [Fact]
        public void MouseWheelMessageNegativeDeltaAllowed()
        {
            var msg = new MouseWheelMessage(-120f, -240f, 0f, 0f);

            msg.DeltaX.Should().Be(-120f);
            msg.DeltaY.Should().Be(-240f);
        }
    }

    public class KeyMessageTests
    {
        [Fact]
        public void KeyMessagePropertiesSetCorrectly()
        {
            var msg = new KeyMessage(65, KeyModifiers.None); // 'A' key

            msg.KeyCode.Should().Be(65);
            msg.Modifiers.Should().Be(KeyModifiers.None);
        }

        [Fact]
        public void KeyModifiersFlagsCombineCorrectly()
        {
            var ctrlShift = KeyModifiers.Control | KeyModifiers.Shift;

            ctrlShift.HasFlag(KeyModifiers.Control).Should().BeTrue();
            ctrlShift.HasFlag(KeyModifiers.Shift).Should().BeTrue();
            ctrlShift.HasFlag(KeyModifiers.Alt).Should().BeFalse();
        }

        [Fact]
        public void KeyModifiersAllValuesDefinedCorrectly()
        {
            KeyModifiers.None.Should().Be((KeyModifiers)0);
            KeyModifiers.Shift.Should().Be((KeyModifiers)1);
            KeyModifiers.Control.Should().Be((KeyModifiers)2);
            KeyModifiers.Alt.Should().Be((KeyModifiers)4);
            KeyModifiers.Win.Should().Be((KeyModifiers)8);
        }

        [Fact]
        public void KeyMessageWithAllModifiersWorksCorrectly()
        {
            var allModifiers = KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Win;
            var msg = new KeyMessage(65, allModifiers);

            msg.Modifiers.HasFlag(KeyModifiers.Shift).Should().BeTrue();
            msg.Modifiers.HasFlag(KeyModifiers.Control).Should().BeTrue();
            msg.Modifiers.HasFlag(KeyModifiers.Alt).Should().BeTrue();
            msg.Modifiers.HasFlag(KeyModifiers.Win).Should().BeTrue();
        }
    }

    public class ChatMessageTests
    {
        [Fact]
        public void ChatMessagePropertiesSetCorrectly()
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var msg = new ChatMessage("sender-123", "John Doe", "Hello, world!", timestamp);

            msg.SenderClientId.Should().Be("sender-123");
            msg.SenderDisplayName.Should().Be("John Doe");
            msg.Text.Should().Be("Hello, world!");
            msg.TimestampUtc.Should().Be(timestamp);
        }

        [Fact]
        public void ChatMessageEmptyTextAllowed()
        {
            var msg = new ChatMessage("sender", "Name", "", 0);

            msg.Text.Should().BeEmpty();
        }
    }

    public class FileTransferMessagesTests
    {
        [Fact]
        public void FileSendRequestMessagePropertiesSetCorrectly()
        {
            var msg = new FileSendRequestMessage("transfer-123", "document.pdf", 1024 * 1024);

            msg.TransferId.Should().Be("transfer-123");
            msg.FileName.Should().Be("document.pdf");
            msg.FileSize.Should().Be(1024 * 1024);
        }

        [Fact]
        public void FileSendResponseMessageAcceptedProperties()
        {
            var msg = new FileSendResponseMessage("transfer-123", true, null);

            msg.TransferId.Should().Be("transfer-123");
            msg.Accepted.Should().BeTrue();
            msg.ErrorMessage.Should().BeNull();
        }

        [Fact]
        public void FileSendResponseMessageRejectedProperties()
        {
            var msg = new FileSendResponseMessage("transfer-123", false, "File too large");

            msg.Accepted.Should().BeFalse();
            msg.ErrorMessage.Should().Be("File too large");
        }

        [Fact]
        public void FileChunkMessagePropertiesSetCorrectly()
        {
            var data = new byte[] { 1, 2, 3, 4, 5 };
            var msg = new FileChunkMessage("transfer-123", 0, 10, data);

            msg.TransferId.Should().Be("transfer-123");
            msg.ChunkIndex.Should().Be(0);
            msg.TotalChunks.Should().Be(10);
            msg.Data.ToArray().Should().Equal(data);
        }

        [Fact]
        public void FileCompleteMessagePropertiesSetCorrectly()
        {
            var msg = new FileCompleteMessage("transfer-123");

            msg.TransferId.Should().Be("transfer-123");
        }

        [Fact]
        public void FileCancelMessagePropertiesSetCorrectly()
        {
            var msg = new FileCancelMessage("transfer-123", "User cancelled");

            msg.TransferId.Should().Be("transfer-123");
            msg.Reason.Should().Be("User cancelled");
        }

        [Fact]
        public void FileErrorMessagePropertiesSetCorrectly()
        {
            var msg = new FileErrorMessage("transfer-123", "Disk full");

            msg.TransferId.Should().Be("transfer-123");
            msg.ErrorMessage.Should().Be("Disk full");
        }
    }

    public class ClipboardMessagesTests
    {
        [Fact]
        public void ClipboardTextMessagePropertiesSetCorrectly()
        {
            var msg = new ClipboardTextMessage("Hello, clipboard!");

            msg.Text.Should().Be("Hello, clipboard!");
        }

        [Fact]
        public void ClipboardTextMessageLargeTextAllowed()
        {
            var largeText = new string('x', 100000);
            var msg = new ClipboardTextMessage(largeText);

            msg.Text.Should().HaveLength(100000);
        }

        [Fact]
        public void ClipboardImageMessagePropertiesSetCorrectly()
        {
            var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes
            var msg = new ClipboardImageMessage(imageData);

            msg.Data.ToArray().Should().Equal(imageData);
        }
    }

    public class FrameMessageTests
    {
        [Fact]
        public void FrameMessagePropertiesSetCorrectly()
        {
            var regions = new[]
            {
                new FrameRegion(true, 0, 0, 1920, 1080, new byte[] { 1, 2, 3 })
            };

            var msg = new FrameMessage("display-1", 42, FrameCodec.Jpeg90, regions);

            msg.DisplayId.Should().Be("display-1");
            msg.FrameNumber.Should().Be(42UL);
            msg.Codec.Should().Be(FrameCodec.Jpeg90);
            msg.Regions.Should().HaveCount(1);
        }

        [Fact]
        public void FrameRegionPropertiesSetCorrectly()
        {
            var data = new byte[] { 0xFF, 0xD8, 0xFF }; // JPEG magic bytes
            var region = new FrameRegion(true, 100, 200, 800, 600, data);

            region.IsKeyframe.Should().BeTrue();
            region.X.Should().Be(100);
            region.Y.Should().Be(200);
            region.Width.Should().Be(800);
            region.Height.Should().Be(600);
            region.Data.ToArray().Should().Equal(data);
        }

        [Fact]
        public void FrameCodecJpeg90HasCorrectValue()
        {
            FrameCodec.Jpeg90.Should().Be((FrameCodec)0);
        }
    }

    public class MessageTypesTests
    {
        [Fact]
        public void DisplayMessageTypesDefinedCorrectly()
        {
            MessageTypes.Display.Switch.Should().Be("display.switch");
            MessageTypes.Display.Select.Should().Be("display.select");
        }

        [Fact]
        public void ScreenMessageTypesDefinedCorrectly()
        {
            MessageTypes.Screen.Frame.Should().Be("screen.frame");
        }

        [Fact]
        public void InputMessageTypesDefinedCorrectly()
        {
            MessageTypes.Input.KeyDown.Should().Be("input.key.down");
            MessageTypes.Input.KeyUp.Should().Be("input.key.up");
            MessageTypes.Input.MouseMove.Should().Be("input.mouse.move");
            MessageTypes.Input.MouseDown.Should().Be("input.mouse.down");
            MessageTypes.Input.MouseUp.Should().Be("input.mouse.up");
            MessageTypes.Input.MouseWheel.Should().Be("input.mouse.wheel");
            MessageTypes.Input.SecureAttentionSequence.Should().Be("input.sas");
        }

        [Fact]
        public void FileTransferMessageTypesDefinedCorrectly()
        {
            MessageTypes.FileTransfer.SendRequest.Should().Be("file.send.request");
            MessageTypes.FileTransfer.SendResponse.Should().Be("file.send.response");
            MessageTypes.FileTransfer.Chunk.Should().Be("file.chunk");
            MessageTypes.FileTransfer.Complete.Should().Be("file.complete");
            MessageTypes.FileTransfer.Cancel.Should().Be("file.cancel");
            MessageTypes.FileTransfer.Error.Should().Be("file.error");
        }

        [Fact]
        public void ClipboardMessageTypesDefinedCorrectly()
        {
            MessageTypes.Clipboard.Text.Should().Be("clipboard.text");
            MessageTypes.Clipboard.Image.Should().Be("clipboard.image");
        }

        [Fact]
        public void ChatMessageTypesDefinedCorrectly()
        {
            MessageTypes.Chat.Message.Should().Be("chat.message");
        }
    }

    public class TryConnectErrorTests
    {
        [Fact]
        public void TryConnectErrorAllValuesDefined()
        {
            var values = Enum.GetValues<TryConnectError>();

            values.Should().Contain(TryConnectError.ViewerNotFound);
            values.Should().Contain(TryConnectError.IncorrectUsernameOrPassword);
            values.Should().Contain(TryConnectError.CannotConnectToYourself);
        }
    }

    public class MessageDestinationTests
    {
        [Fact]
        public void MessageDestinationAllValuesDefined()
        {
            var values = Enum.GetValues<MessageDestination>();

            values.Should().Contain(MessageDestination.PresenterOnly);
            values.Should().Contain(MessageDestination.AllViewers);
            values.Should().Contain(MessageDestination.All);
            values.Should().Contain(MessageDestination.AllExceptSender);
            values.Should().Contain(MessageDestination.SpecificClients);
        }
    }
}
