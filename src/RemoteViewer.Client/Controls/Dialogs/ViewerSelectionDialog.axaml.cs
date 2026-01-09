using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RemoteViewer.Client.Controls.Dialogs;

public partial class ViewerSelectionDialog : Window
{
    private ViewerSelectionDialogViewModel? _viewModel;

    public ViewerSelectionDialog()
    {
        this.InitializeComponent();
        this.DataContextChanged += (_, _) => this._viewModel = this.DataContext as ViewerSelectionDialogViewModel;
    }

    private void OnSendClicked(object? sender, RoutedEventArgs e)
    {
        if (this._viewModel is null)
        {
            this.Close(null);
            return;
        }

        var selected = this._viewModel.Viewers
            .Where(v => v.IsSelected)
            .Select(v => v.ClientId)
            .ToList();

        this.Close(selected);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        this.Close(null);
    }
}
