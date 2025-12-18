using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RemoteViewer.Client.Views.Presenter;

public partial class PresenterViewerDisplay : ObservableObject
{
    public PresenterViewerDisplay(string clientId, string displayName)
    {
        this.ClientId = clientId;
        this.DisplayName = displayName;
    }

    public string ClientId { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isInputBlocked;

    [ObservableProperty]
    private bool _isSelected = true;

    [RelayCommand]
    private void ToggleInputBlock()
    {
        this.IsInputBlocked = !this.IsInputBlocked;
    }
}
