using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Common;
using RemoteViewer.Server.SharedAPI;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.HubClient;

internal interface IChatServiceImpl
{
    void HandleChatMessage(ChatMessage message);
}

public sealed partial class ChatService : IDisposable, IChatServiceImpl
{
    public const int MaxMessageLength = 1000;

    // Matches URLs with scheme (http/https), www prefix, or common TLDs
    [GeneratedRegex(@"(https?://|www\.)[^\s<>""']+|[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?\.(?:com|org|net|edu|gov|io|co|dev|app|me|info|biz|de|uk|fr|it|es|nl|be|at|ch|ru|cn|jp|au|ca|br|in|pl|se|no|dk|fi)\b[^\s<>""']*", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    private readonly Connection _connection;
    private readonly ILogger<ChatService> _logger;

    private readonly List<ChatMessageDisplay> _messages = [];
    private readonly Lock _messagesLock = new();

    private int _disposed;

    public ChatService(Connection connection, ILogger<ChatService> logger)
    {
        this._connection = connection;
        this._logger = logger;
        this._logger.ChatServiceStarted();
    }

    public event EventHandler<ChatMessageDisplay>? MessageReceived;

    public IReadOnlyList<ChatMessageDisplay> GetMessages()
    {
        using (this._messagesLock.EnterScope())
        {
            return this._messages.ToList().AsReadOnly();
        }
    }

    public async Task SendMessageAsync(string text)
    {
        if (this._connection.IsClosed || string.IsNullOrWhiteSpace(text))
            return;

        var trimmedText = text.Trim();
        if (trimmedText.Length > MaxMessageLength)
            trimmedText = trimmedText[..MaxMessageLength];

        var ownClientId = this._connection.Owner.ClientId;
        var ownDisplayName = this._connection.Owner.DisplayName;

        if (ownClientId is null || ownDisplayName is null)
            return;

        var message = new ChatMessage(
            ownClientId,
            ownDisplayName,
            trimmedText,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        using var buffer = PooledBufferWriter.Rent();
        ProtocolSerializer.Serialize(buffer, message);

        await this._connection.Owner.SendMessageAsync(
            this._connection.ConnectionId,
            MessageTypes.Chat.Message,
            buffer.WrittenMemory,
            MessageDestination.AllExceptSender,
            null);

        this._logger.ChatMessageSent(text.Length);

        // Add to local history immediately for responsiveness
        this.AddMessageToHistory(message, isSelf: true);
    }

    void IChatServiceImpl.HandleChatMessage(ChatMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Text))
            return;

        this._logger.ChatMessageReceived(message.SenderDisplayName, message.Text.Length);
        this.AddMessageToHistory(message, isSelf: false);
    }

    private void AddMessageToHistory(ChatMessage message, bool isSelf)
    {
        var presenterClientId = this._connection.Presenter?.ClientId;
        var isFromPresenter = string.Equals(message.SenderClientId, presenterClientId, StringComparison.Ordinal);

        // Extract URLs from message text with domain info
        var links = UrlRegex()
            .Matches(message.Text)
            .Select(m => m.Value)
            .Distinct()
            .Select(url =>
            {
                // Ensure URL has a scheme for opening in browser
                var fullUrl = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                              url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    ? url
                    : "https://" + url;

                var domain = Uri.TryCreate(fullUrl, UriKind.Absolute, out var uri)
                    ? uri.Host
                    : url;
                return new ChatLinkDisplay(domain, fullUrl);
            })
            .ToList();

        var displayMessage = new ChatMessageDisplay(
            message.SenderDisplayName,
            isFromPresenter,
            isSelf,
            message.Text,
            DateTimeOffset.FromUnixTimeMilliseconds(message.TimestampUtc).LocalDateTime,
            links);

        using (this._messagesLock.EnterScope())
        {
            // Insert in sorted order by timestamp
            var insertIndex = this._messages.Count;
            for (var i = this._messages.Count - 1; i >= 0; i--)
            {
                if (this._messages[i].Timestamp <= displayMessage.Timestamp)
                {
                    insertIndex = i + 1;
                    break;
                }
                insertIndex = i;
            }
            this._messages.Insert(insertIndex, displayMessage);
        }

        this.MessageReceived?.Invoke(this, displayMessage);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) == 1)
            return;

        this._logger.ChatServiceStopped();
    }
}

public sealed record ChatMessageDisplay(
    string SenderDisplayName,
    bool IsFromPresenter,
    bool IsFromSelf,
    string Text,
    DateTime Timestamp,
    IReadOnlyList<ChatLinkDisplay> Links);

public sealed record ChatLinkDisplay(string Domain, string Url);
