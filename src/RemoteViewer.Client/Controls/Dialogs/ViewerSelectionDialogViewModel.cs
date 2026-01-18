using CommunityToolkit.Mvvm.ComponentModel;
using RemoteViewer.Client.Views.Presenter;

namespace RemoteViewer.Client.Controls.Dialogs;

public class ViewerSelectionDialogViewModel : ObservableObject
{
    public IReadOnlyList<PresenterViewerDisplay> Viewers { get; }
    public string FileName { get; }
    public string FileSizeFormatted { get; }

    public ViewerSelectionDialogViewModel(
        IReadOnlyList<PresenterViewerDisplay> viewers,
        string fileName,
        string fileSizeFormatted)
    {
        this.Viewers = viewers;
        this.FileName = fileName;
        this.FileSizeFormatted = fileSizeFormatted;
    }
}
