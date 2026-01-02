using Avalonia.Controls;
using Avalonia.Interactivity;
using RemoteViewer.Client.Views.Presenter;

namespace RemoteViewer.Client.Controls.Dialogs;

public partial class ViewerSelectionDialog : Window
{
    private IReadOnlyList<PresenterViewerDisplay> _viewers = [];

    public ViewerSelectionDialog()
    {
        this.InitializeComponent();
    }

    public static ViewerSelectionDialog Create(
        IReadOnlyList<PresenterViewerDisplay> viewers,
        string fileName,
        string fileSizeFormatted)
    {
        var dialog = new ViewerSelectionDialog
        {
            _viewers = viewers
        };
        dialog.FileNameText.Text = fileName;
        dialog.FileSizeText.Text = fileSizeFormatted;
        dialog.ViewerList.ItemsSource = viewers;
        return dialog;
    }

    private void OnSendClicked(object? sender, RoutedEventArgs e)
    {
        var selected = this._viewers
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
