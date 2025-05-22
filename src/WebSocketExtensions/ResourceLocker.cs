using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketExtensions
{
    public class ResourceLocker
    {
        private ConcurrentDictionary<object, SemaphoreSlim> _lockers = null;

        public ResourceLocker()
        {
            _lockers = new ConcurrentDictionary<object, SemaphoreSlim>();
        }

        public Task EnterLockAsync(object resource, CancellationToken ct)
        {
            SemaphoreSlim ss= _lockers.GetOrAdd(resource, new SemaphoreSlim(1, 1));
            return ss.WaitAsync(ct);
        }

        internal void RemoveLock(object resource)
        {
            SemaphoreSlim ss;
            if (_lockers.TryRemove(resource, out ss))
            {
                ss.Dispose();
            }
        }

        public bool ExitLock(object resource)
        {
            SemaphoreSlim ss = null;
            if (_lockers.TryGetValue(resource, out ss))
            {
                try
                {
                    ss.Release();
                }catch (ObjectDisposedException) { }
            }

            return true;
        }
    }
}
