namespace RemoteViewer.Server.Common;

public static class ReaderWriterLockSlimExtensions
{
    extension(ReaderWriterLockSlim self)
    {
        public WriteLockDisposable WriteLock()
        {
            return new WriteLockDisposable(self);
        }

        public ReadLockDisposable ReadLock()
        {
            return new ReadLockDisposable(self);
        }

        public UpgradeableReadLockDisposable UpgradeableReadLock()
        {
            return new UpgradeableReadLockDisposable(self);
        }
    }

    public struct WriteLockDisposable : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        public WriteLockDisposable(ReaderWriterLockSlim rwLock)
        {
            _lock = rwLock;
            _lock.EnterWriteLock();
        }
        public void Dispose()
        {
            _lock.ExitWriteLock();
        }
    }

    public struct ReadLockDisposable : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        public ReadLockDisposable(ReaderWriterLockSlim rwLock)
        {
            _lock = rwLock;
            _lock.EnterReadLock();
        }
        public void Dispose()
        {
            _lock.ExitReadLock();
        }
    }

    public struct UpgradeableReadLockDisposable : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        public UpgradeableReadLockDisposable(ReaderWriterLockSlim rwLock)
        {
            _lock = rwLock;
            _lock.EnterUpgradeableReadLock();
        }
        public void Dispose()
        {
            _lock.ExitUpgradeableReadLock();
        }
    }
}
