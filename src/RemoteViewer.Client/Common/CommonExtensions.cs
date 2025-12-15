using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;
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
}
