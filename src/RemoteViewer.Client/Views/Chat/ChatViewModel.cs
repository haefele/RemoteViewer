using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.HubClient;

namespace RemoteViewer.Client.Views.Chat;

public partial class ChatViewModel : ObservableObject, IDisposable
{
    private readonly ChatService _chatService;
    private readonly ILogger<ChatViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CharactersRemaining))]
    [NotifyPropertyChangedFor(nameof(IsNearLimit))]
    [NotifyPropertyChangedFor(nameof(IsAtLimit))]
    private string _messageInput = string.Empty;

    [ObservableProperty]
    private bool _hasUnreadMessages;

    [ObservableProperty]
    private bool _isOpen;

    public event EventHandler? OpenChatRequested;

    public int MaxLength => ChatService.MaxMessageLength;
    public int CharactersRemaining => this.MaxLength - this.MessageInput.Length;
    public bool IsNearLimit => this.MessageInput.Length >= this.MaxLength - 100;
    public bool IsAtLimit => this.MessageInput.Length >= this.MaxLength;

    public ObservableCollection<ChatMessageDisplay> Messages { get; } = [];

    private int _disposed;

    public ChatViewModel(ChatService chatService, ILogger<ChatViewModel> logger)
    {
        this._chatService = chatService;
        this._logger = logger;
        this._chatService.MessageReceived += this.OnMessageReceived;

        // Load any existing messages
        foreach (var message in this._chatService.GetMessages())
        {
            this.Messages.Add(message);
        }
    }

    public void RequestOpenChat()
    {
        this.OpenChatRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsOpenChanged(bool value)
    {
        if (value)
            this.HasUnreadMessages = false;
    }

    private void OnMessageReceived(object? sender, ChatMessageDisplay message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            this.Messages.Add(message);

            if (!this.IsOpen && !message.IsFromSelf)
            {
                this.HasUnreadMessages = true;
            }
        });
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var text = this.MessageInput;
        if (string.IsNullOrWhiteSpace(text))
            return;

        this.MessageInput = string.Empty;
        await this._chatService.SendMessageAsync(text);
    }

    [RelayCommand]
    private void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to open URL: {Url}", url);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) == 1)
            return;

        this._chatService.MessageReceived -= this.OnMessageReceived;
    }
}
