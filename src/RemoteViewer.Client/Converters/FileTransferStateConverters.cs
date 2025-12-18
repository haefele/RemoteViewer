using System.Globalization;
using Avalonia.Data.Converters;
using RemoteViewer.Client.Services.FileTransfer;

namespace RemoteViewer.Client.Converters;

public class FileTransferStateToIsWaitingConverter : IValueConverter
{
    public static readonly FileTransferStateToIsWaitingConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FileTransferState state)
        {
            return state is FileTransferState.Pending or FileTransferState.WaitingForAcceptance;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class FileTransferStateToIsTransferringConverter : IValueConverter
{
    public static readonly FileTransferStateToIsTransferringConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FileTransferState state)
        {
            return state is FileTransferState.Transferring;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToSendingReceivingConverter : IValueConverter
{
    public static readonly BoolToSendingReceivingConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isUpload)
        {
            return isUpload ? "Sending file" : "Receiving file";
        }
        return "Transferring";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
