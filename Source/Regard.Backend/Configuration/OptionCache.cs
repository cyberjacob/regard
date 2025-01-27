﻿using Nito.AsyncEx;
using Regard.Backend.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Regard.Backend.Configuration
{
    public class OptionCache<TKey> : IOptionCache<TKey>
    {
        struct CacheEntry
        {
            public object Value;
            public DateTime Timestamp;
        }

        private const int CacheExpirationSeconds = 3600 * 24;

        private readonly Dictionary<TKey, CacheEntry> cache = new Dictionary<TKey, CacheEntry>();
        private readonly AsyncReaderWriterLock cacheLock = new AsyncReaderWriterLock();

        public bool Get<TValue>(TKey key, out TValue value)
        {
            bool entryFound;

            using (var @lock = cacheLock.ReaderLock())
            {
                entryFound = cache.TryGetValue(key, out CacheEntry entry);
                if (entryFound && entry.Timestamp + TimeSpan.FromSeconds(CacheExpirationSeconds) > DateTime.Now)
                {
                    value = (TValue)entry.Value;
                    return true;
                }
            };

            if (entryFound)
            {
                // cache expired
                using var wLock = cacheLock.WriterLock();
                cache.Remove(key);
            }

            value = default;
            return false;
        }

        public void Set<TValue>(TKey key, TValue value)
        {
            using var @lock = cacheLock.WriterLock();

            cache[key] = new CacheEntry()
            {
                Timestamp = DateTime.Now,
                Value = value
            };
        }

        public void ClearExpired()
        {
            using var @lock = cacheLock.WriterLock();

            var expiredKeys = cache
                .Where(x => x.Value.Timestamp + TimeSpan.FromSeconds(CacheExpirationSeconds) < DateTime.Now)
                .Select(x => x.Key)
                .ToArray();

            foreach (var key in expiredKeys)
                cache.Remove(key);
        }

        public void Invalidate()
        {
            using var @lock = cacheLock.WriterLock();
            cache.Clear();
        }

        public void Remove(TKey key)
        {
            cache.Remove(key);
        }
    }
}
