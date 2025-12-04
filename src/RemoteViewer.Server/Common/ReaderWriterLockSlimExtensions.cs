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
            this._lock = rwLock;
            this._lock.EnterWriteLock();
        }
        public void Dispose()
        {
            this._lock.ExitWriteLock();
        }
    }

    public struct ReadLockDisposable : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        public ReadLockDisposable(ReaderWriterLockSlim rwLock)
        {
            this._lock = rwLock;
            this._lock.EnterReadLock();
        }
        public void Dispose()
        {
            this._lock.ExitReadLock();
        }
    }

    public struct UpgradeableReadLockDisposable : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        public UpgradeableReadLockDisposable(ReaderWriterLockSlim rwLock)
        {
            this._lock = rwLock;
            this._lock.EnterUpgradeableReadLock();
        }
        public void Dispose()
        {
            this._lock.ExitUpgradeableReadLock();
        }
    }
}
