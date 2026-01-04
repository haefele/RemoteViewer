using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Nerdbank.MessagePack;

namespace RemoteViewer.Client.Common;

public static class CommonExtensions
{
    extension<T>(Channel<T> self) where T : IDisposable
    {
        public void DisposeItems()
        {
            while (self.Reader.TryRead(out var item))
            {
                item.Dispose();
            }
        }
    }

    extension(DragEventArgs self)
    {
        public bool IsSingleFileDrag
            => self.DataTransfer.TryGetFiles()?.Take(2).Count() == 1;

        public IStorageFile? SingleFile
            => self.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault();
    }
}
