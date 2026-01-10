using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace RemoteViewer.Client.Views.Chat;

public partial class ChatView : Window
{
    private ChatViewModel? _viewModel;

    public ChatView()
    {
        this.InitializeComponent();
        this.DataContextChanged += this.OnDataContextChanged;
        this.Activated += this.OnActivated;
        this.Deactivated += this.OnDeactivated;
    }

    public void ShowAndActivate()
    {
        this.Show();

        // Restore from minimized state if needed
        if (this.WindowState == WindowState.Minimized)
            this.WindowState = WindowState.Normal;

        this.Activate();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
        {
            this._viewModel.Messages.CollectionChanged -= this.Messages_CollectionChanged;
        }

        this._viewModel = this.DataContext as ChatViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.Messages.CollectionChanged += this.Messages_CollectionChanged;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        Dispatcher.UIThread.Post(() => this.MessageInputBox.Focus(), DispatcherPriority.Background);

        if (this._viewModel is { } vm)
            vm.IsOpen = true;
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (this._viewModel is { } vm)
            vm.IsOpen = true;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // When minimized or deactivated, treat as not open for notification purposes
        if (this._viewModel is { } vm)
            vm.IsOpen = false;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (this._viewModel is { } vm)
        {
            vm.Messages.CollectionChanged -= this.Messages_CollectionChanged;
            vm.IsOpen = false;
            vm.Dispose();
        }
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(() => this.MessageScrollViewer.ScrollToEnd());
        }
    }

    private void MessageInputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || this._viewModel is null)
            return;

        // Shift+Enter or Ctrl+Enter inserts a line break
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var textBox = this.MessageInputBox;
            var caretIndex = textBox.CaretIndex;
            var text = this._viewModel.MessageInput ?? string.Empty;
            this._viewModel.MessageInput = text.Insert(caretIndex, Environment.NewLine);
            textBox.CaretIndex = caretIndex + Environment.NewLine.Length;
            e.Handled = true;
            return;
        }

        // Plain Enter sends the message
        this._viewModel.SendMessageCommand.Execute(null);
        e.Handled = true;
    }
}
