﻿using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketExtensions
{
    public class ResourceLocker
    {
        private Dictionary<object, SemaphoreSlim> _lockers = null;

        private object lockObj = new object();

        public ResourceLocker()
        {
            _lockers = new Dictionary<object, SemaphoreSlim>();
        }
        public void EnterLock(object resource)
        {
            SemaphoreSlim ss;
            lock (lockObj)
            {
                if (!_lockers.ContainsKey(resource))
                {
                    ss = new SemaphoreSlim(1, 1);
                    _lockers.Add(resource, ss);
                }
                else
                {
                    ss = _lockers[resource];
                }

            }
            try
            {
                ss.Wait();
            }
            catch (ArgumentNullException e)
            {
                //disposed of while wait
            }

        }
        public Task EnterLockAsync(object resource, CancellationToken ct)
        {
            SemaphoreSlim ss;
            lock (lockObj)
            {
                if (!_lockers.ContainsKey(resource))
                {
                    ss = new SemaphoreSlim(1, 1);
                    _lockers.Add(resource, ss);
                }
                else
                {
                    ss = _lockers[resource];
                }

            }
            return ss.WaitAsync(ct);
            //return Task.FromResult(true);

        }

        internal void RemoveLock(object resource)
        {
            lock (lockObj)
            {
                if (_lockers.ContainsKey(resource))
                {
                    var ss = _lockers[resource];
                    ss.Dispose();
                    _lockers.Remove(resource);
                }
            }//lock
        }

        public bool ExitLock(object resource)
        {
            SemaphoreSlim ss = null;
            lock (lockObj)
            {
                if (_lockers.ContainsKey(resource))
                {
                    ss = _lockers?[resource];
                }
            }//lock

            if (ss == null)
                return false;

            if(ss.CurrentCount != 1)
                ss?.Release();

            return true;

        }

        //not used, is static
        //public void Dispose()
        //{
        //    foreach(var l in _lockers)
        //    {
        //        l.Value.Dispose();
        //    }
        //}
    }
}
