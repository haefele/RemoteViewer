using RemoteViewer.Shared;
using RemoteViewer.Shared.Protocol;

namespace RemoteViewer.Shared.Tests.Protocol;

public class DisplayInfoTests
{
    [Test]
    [Arguments(0, 0, 1920, 1080, 1920, 1080)]
    [Arguments(0, 0, 2560, 1440, 2560, 1440)]
    [Arguments(-1920, 0, 0, 1080, 1920, 1080)]
    [Arguments(1920, 0, 3840, 1080, 1920, 1080)]
    public async Task DisplayInfoWidthAndHeightCalculatedCorrectly(
        int left, int top, int right, int bottom,
        int expectedWidth, int expectedHeight)
    {
        // Act
        var displayInfo = new DisplayInfo("id", "Display", true, left, top, right, bottom);

        // Assert
        await Assert.That(displayInfo.Width).IsEqualTo(expectedWidth);
        await Assert.That(displayInfo.Height).IsEqualTo(expectedHeight);
    }

    [Test]
    public async Task DisplayInfoRecordEqualityWorksCorrectly()
    {
        // Act
        var display1 = new DisplayInfo("id1", "Display 1", true, 0, 0, 1920, 1080);
        var display2 = new DisplayInfo("id1", "Display 1", true, 0, 0, 1920, 1080);
        var display3 = new DisplayInfo("id2", "Display 2", false, 0, 0, 1920, 1080);

        // Assert
        await Assert.That(display1).IsEqualTo(display2);
        await Assert.That(display1).IsNotEqualTo(display3);
    }
}

public class ClientInfoTests
{
    [Test]
    public async Task ClientInfoPropertiesSetCorrectly()
    {
        // Act
        var clientInfo = new ClientInfo("client-123", "John Doe");

        // Assert
        await Assert.That(clientInfo.ClientId).IsEqualTo("client-123");
        await Assert.That(clientInfo.DisplayName).IsEqualTo("John Doe");
    }

    [Test]
    public async Task ClientInfoWithEmptyDisplayNameAllowsEmpty()
    {
        // Act
        var clientInfo = new ClientInfo("client-123", "");

        // Assert
        await Assert.That(clientInfo.DisplayName).IsEmpty();
    }
}

public class ConnectionPropertiesTests
{
    [Test]
    public async Task ConnectionPropertiesDefaultValuesSetCorrectly()
    {
        // Act
        var props = new ConnectionProperties(
            CanSendSecureAttentionSequence: false,
            InputBlockedViewerIds: [],
            AvailableDisplays: []
        );

        // Assert
        await Assert.That(props.CanSendSecureAttentionSequence).IsFalse();
        await Assert.That(props.InputBlockedViewerIds).IsEmpty();
        await Assert.That(props.AvailableDisplays).IsEmpty();
    }

    [Test]
    public async Task ConnectionPropertiesWithValuesSetCorrectly()
    {
        var displays = new List<DisplayInfo>
        {
            new("d1", "Primary", true, 0, 0, 1920, 1080),
            new("d2", "Secondary", false, 1920, 0, 3840, 1080)
        };
        var blockedIds = new List<string> { "viewer-1", "viewer-2" };

        // Act
        var props = new ConnectionProperties(
            CanSendSecureAttentionSequence: true,
            InputBlockedViewerIds: blockedIds,
            AvailableDisplays: displays
        );

        // Assert
        await Assert.That(props.CanSendSecureAttentionSequence).IsTrue();
        await Assert.That(props.InputBlockedViewerIds).Count().IsEqualTo(2);
        await Assert.That(props.AvailableDisplays).Count().IsEqualTo(2);
    }
}

public class ConnectionInfoTests
{
    [Test]
    public async Task ConnectionInfoAllPropertiesSetCorrectly()
    {
        var presenter = new ClientInfo("presenter-1", "Presenter");
        var viewers = new List<ClientInfo>
        {
            new("viewer-1", "Viewer 1"),
            new("viewer-2", "Viewer 2")
        };
        var props = new ConnectionProperties(false, [], []);

        // Act
        var connectionInfo = new ConnectionInfo("conn-123", presenter, viewers, props);

        // Assert
        await Assert.That(connectionInfo.ConnectionId).IsEqualTo("conn-123");
        await Assert.That(connectionInfo.Presenter).IsEqualTo(presenter);
        await Assert.That(connectionInfo.Viewers).Count().IsEqualTo(2);
        await Assert.That(connectionInfo.Properties).IsEqualTo(props);
    }
}

public class MouseMessagesTests
{
    [Test]
    public async Task MouseMoveMessagePropertiesSetCorrectly()
    {
        // Act
        var msg = new MouseMoveMessage(100.5f, 200.75f);

        // Assert
        await Assert.That(msg.X).IsEqualTo(100.5f);
        await Assert.That(msg.Y).IsEqualTo(200.75f);
    }

    [Test]
    public async Task MouseButtonMessageAllButtonsValidValues()
    {
        // Act
        var buttons = Enum.GetValues<MouseButton>();

        // Assert
        await Assert.That(buttons).Contains(MouseButton.Left);
        await Assert.That(buttons).Contains(MouseButton.Right);
        await Assert.That(buttons).Contains(MouseButton.Middle);
        await Assert.That(buttons).Contains(MouseButton.XButton1);
        await Assert.That(buttons).Contains(MouseButton.XButton2);
    }

    [Test]
    [Arguments(MouseButton.Left)]
    [Arguments(MouseButton.Right)]
    [Arguments(MouseButton.Middle)]
    public async Task MouseButtonMessagePropertiesSetCorrectly(MouseButton button)
    {
        // Act
        var msg = new MouseButtonMessage(button, 50.0f, 75.0f);

        // Assert
        await Assert.That(msg.Button).IsEqualTo(button);
        await Assert.That(msg.X).IsEqualTo(50.0f);
        await Assert.That(msg.Y).IsEqualTo(75.0f);
    }

    [Test]
    public async Task MouseWheelMessagePropertiesSetCorrectly()
    {
        // Act
        var msg = new MouseWheelMessage(0f, 120f, 100f, 200f);

        // Assert
        await Assert.That(msg.DeltaX).IsEqualTo(0f);
        await Assert.That(msg.DeltaY).IsEqualTo(120f);
        await Assert.That(msg.X).IsEqualTo(100f);
        await Assert.That(msg.Y).IsEqualTo(200f);
    }

    [Test]
    public async Task MouseWheelMessageNegativeDeltaAllowed()
    {
        // Act
        var msg = new MouseWheelMessage(-120f, -240f, 0f, 0f);

        // Assert
        await Assert.That(msg.DeltaX).IsEqualTo(-120f);
        await Assert.That(msg.DeltaY).IsEqualTo(-240f);
    }
}

public class KeyMessageTests
{
    [Test]
    public async Task KeyMessagePropertiesSetCorrectly()
    {
        // Act
        var msg = new KeyMessage(65, KeyModifiers.None); // 'A' key

        // Assert
        await Assert.That((int)msg.KeyCode).IsEqualTo(65);
        await Assert.That(msg.Modifiers).IsEqualTo(KeyModifiers.None);
    }

    [Test]
    public async Task KeyModifiersFlagsCombineCorrectly()
    {
        // Act
        var ctrlShift = KeyModifiers.Control | KeyModifiers.Shift;

        // Assert
        await Assert.That(ctrlShift.HasFlag(KeyModifiers.Control)).IsTrue();
        await Assert.That(ctrlShift.HasFlag(KeyModifiers.Shift)).IsTrue();
        await Assert.That(ctrlShift.HasFlag(KeyModifiers.Alt)).IsFalse();
    }

    [Test]
    public async Task KeyMessageWithAllModifiersWorksCorrectly()
    {
        var allModifiers = KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Win;

        // Act
        var msg = new KeyMessage(65, allModifiers);

        // Assert
        await Assert.That(msg.Modifiers.HasFlag(KeyModifiers.Shift)).IsTrue();
        await Assert.That(msg.Modifiers.HasFlag(KeyModifiers.Control)).IsTrue();
        await Assert.That(msg.Modifiers.HasFlag(KeyModifiers.Alt)).IsTrue();
        await Assert.That(msg.Modifiers.HasFlag(KeyModifiers.Win)).IsTrue();
    }
}

public class ChatMessageTests
{
    [Test]
    public async Task ChatMessagePropertiesSetCorrectly()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Act
        var msg = new ChatMessage("sender-123", "John Doe", "Hello, world!", timestamp);

        // Assert
        await Assert.That(msg.SenderClientId).IsEqualTo("sender-123");
        await Assert.That(msg.SenderDisplayName).IsEqualTo("John Doe");
        await Assert.That(msg.Text).IsEqualTo("Hello, world!");
        await Assert.That(msg.TimestampUtc).IsEqualTo(timestamp);
    }

    [Test]
    public async Task ChatMessageEmptyTextAllowed()
    {
        // Act
        var msg = new ChatMessage("sender", "Name", "", 0);

        // Assert
        await Assert.That(msg.Text).IsEmpty();
    }
}

public class FileTransferMessagesTests
{
    [Test]
    public async Task FileSendRequestMessagePropertiesSetCorrectly()
    {
        // Act
        var msg = new FileSendRequestMessage("transfer-123", "document.pdf", 1024 * 1024);

        // Assert
        await Assert.That(msg.TransferId).IsEqualTo("transfer-123");
        await Assert.That(msg.FileName).IsEqualTo("document.pdf");
        await Assert.That(msg.FileSize).IsEqualTo(1024 * 1024);
    }

    [Test]
    public async Task FileSendResponseMessageAcceptedProperties()
    {
        // Act
        var msg = new FileSendResponseMessage("transfer-123", true, null);

        // Assert
        await Assert.That(msg.TransferId).IsEqualTo("transfer-123");
        await Assert.That(msg.Accepted).IsTrue();
        await Assert.That(msg.ErrorMessage).IsNull();
    }

    [Test]
    public async Task FileSendResponseMessageRejectedProperties()
    {
        // Act
        var msg = new FileSendResponseMessage("transfer-123", false, "File too large");

        // Assert
        await Assert.That(msg.Accepted).IsFalse();
        await Assert.That(msg.ErrorMessage).IsEqualTo("File too large");
    }

    [Test]
    public async Task FileChunkMessagePropertiesSetCorrectly()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var msg = new FileChunkMessage("transfer-123", 0, 10, data);

        // Assert
        await Assert.That(msg.TransferId).IsEqualTo("transfer-123");
        await Assert.That(msg.ChunkIndex).IsEqualTo(0);
        await Assert.That(msg.TotalChunks).IsEqualTo(10);
        await Assert.That(msg.Data.ToArray()).IsEquivalentTo(data);
    }

    [Test]
    public async Task FileCompleteMessagePropertiesSetCorrectly()
    {
        // Act
        var msg = new FileCompleteMessage("transfer-123");

        // Assert
        await Assert.That(msg.TransferId).IsEqualTo("transfer-123");
    }

    [Test]
    public async Task FileCancelMessagePropertiesSetCorrectly()
    {
        // Act
        var msg = new FileCancelMessage("transfer-123", "User cancelled");

        // Assert
        await Assert.That(msg.TransferId).IsEqualTo("transfer-123");
        await Assert.That(msg.Reason).IsEqualTo("User cancelled");
    }

    [Test]
    public async Task FileErrorMessagePropertiesSetCorrectly()
    {
        // Act
        var msg = new FileErrorMessage("transfer-123", "Disk full");

        // Assert
        await Assert.That(msg.TransferId).IsEqualTo("transfer-123");
        await Assert.That(msg.ErrorMessage).IsEqualTo("Disk full");
    }
}

public class ClipboardMessagesTests
{
    [Test]
    public async Task ClipboardTextMessagePropertiesSetCorrectly()
    {
        // Act
        var msg = new ClipboardTextMessage("Hello, clipboard!");

        // Assert
        await Assert.That(msg.Text).IsEqualTo("Hello, clipboard!");
    }

    [Test]
    public async Task ClipboardTextMessageLargeTextAllowed()
    {
        var largeText = new string('x', 100000);

        // Act
        var msg = new ClipboardTextMessage(largeText);

        // Assert
        await Assert.That(msg.Text).Length().IsEqualTo(100000);
    }

    [Test]
    public async Task ClipboardImageMessagePropertiesSetCorrectly()
    {
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes

        // Act
        var msg = new ClipboardImageMessage(imageData);

        // Assert
        await Assert.That(msg.Data.ToArray()).IsEquivalentTo(imageData);
    }
}

public class FrameMessageTests
{
    [Test]
    public async Task FrameMessagePropertiesSetCorrectly()
    {
        var regions = new[]
        {
            new FrameRegion(true, 0, 0, 1920, 1080, new byte[] { 1, 2, 3 })
        };

        // Act
        var msg = new FrameMessage("display-1", 42, FrameCodec.Jpeg90, regions);

        // Assert
        await Assert.That(msg.DisplayId).IsEqualTo("display-1");
        await Assert.That(msg.FrameNumber).IsEqualTo(42UL);
        await Assert.That(msg.Codec).IsEqualTo(FrameCodec.Jpeg90);
        await Assert.That(msg.Regions).Count().IsEqualTo(1);
    }

    [Test]
    public async Task FrameRegionPropertiesSetCorrectly()
    {
        var data = new byte[] { 0xFF, 0xD8, 0xFF }; // JPEG magic bytes

        // Act
        var region = new FrameRegion(true, 100, 200, 800, 600, data);

        // Assert
        await Assert.That(region.IsKeyframe).IsTrue();
        await Assert.That(region.X).IsEqualTo(100);
        await Assert.That(region.Y).IsEqualTo(200);
        await Assert.That(region.Width).IsEqualTo(800);
        await Assert.That(region.Height).IsEqualTo(600);
        await Assert.That(region.Data.ToArray()).IsEquivalentTo(data);
    }

}

public class TryConnectErrorTests
{
    [Test]
    public async Task TryConnectErrorAllValuesDefined()
    {
        // Act
        var values = Enum.GetValues<TryConnectError>();

        // Assert
        await Assert.That(values).Contains(TryConnectError.ViewerNotFound);
        await Assert.That(values).Contains(TryConnectError.IncorrectUsernameOrPassword);
        await Assert.That(values).Contains(TryConnectError.CannotConnectToYourself);
        await Assert.That(values).Contains(TryConnectError.NotAuthenticated);
    }
}

public class MessageDestinationTests
{
    [Test]
    public async Task MessageDestinationAllValuesDefined()
    {
        // Act
        var values = Enum.GetValues<MessageDestination>();

        // Assert
        await Assert.That(values).Contains(MessageDestination.PresenterOnly);
        await Assert.That(values).Contains(MessageDestination.AllViewers);
        await Assert.That(values).Contains(MessageDestination.All);
        await Assert.That(values).Contains(MessageDestination.AllExceptSender);
        await Assert.That(values).Contains(MessageDestination.SpecificClients);
    }
}
