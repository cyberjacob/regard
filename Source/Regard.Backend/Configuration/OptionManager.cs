﻿using Regard.Backend.Model;
using Regard.Backend.DB;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Configuration;
using Regard.Backend.Common.Model;

namespace Regard.Backend.Configuration
{
    public class OptionManager : IOptionManager
    {
        public struct UserCacheKey 
        {
            public string UserId { get; set; }
            public string Key { get; set; }

            public UserCacheKey(string userId, string key)
            {
                this.UserId = userId;
                this.Key = key;
            }
        }

        public struct SubscriptionCacheKey
        {
            public int SubscriptionId { get; set; }
            public string Key { get; set; }

            public SubscriptionCacheKey(int subscriptionId, string key)
            {
                this.SubscriptionId = subscriptionId;
                this.Key = key;
            }
        }

        public struct SubscriptionFolderCacheKey
        {
            public int FolderId { get; set; }
            public string Key { get; set; }

            public SubscriptionFolderCacheKey(int folderId, string key)
            {
                this.FolderId = folderId;
                this.Key = key;
            }
        }

        private readonly DataContext dataContext;
        private readonly IConfiguration configuration;
        private readonly IOptionCache<string> globalCache;
        private readonly IOptionCache<UserCacheKey> userCache;
        private readonly IOptionCache<SubscriptionCacheKey> subCache;
        private readonly IOptionCache<SubscriptionFolderCacheKey> folderCache;

        public OptionManager(DataContext dataContext,
                             IConfiguration configuration,
                             IOptionCache<string> globalCache,
                             IOptionCache<UserCacheKey> userCache,
                             IOptionCache<SubscriptionCacheKey> subCache,
                             IOptionCache<SubscriptionFolderCacheKey> folderCache)
        {
            this.dataContext = dataContext;
            this.configuration = configuration;
            this.globalCache = globalCache;
            this.userCache = userCache;
            this.subCache = subCache;
            this.folderCache = folderCache;
        }

        /// <summary>
        /// Gets the value of a global option.
        /// </summary>
        /// <remarks>
        /// Options are resolved in this order: cache, database, environment variable, appsettings.json, default value
        /// </remarks>
        /// <typeparam name="TValue">Data type of option</typeparam>
        /// <param name="pref">Option definition</param>
        /// <returns>Option value</returns>
        public TValue GetGlobal<TValue>(OptionDefinition<TValue> pref)
        {
            // Correct order is: database, environment variable, appsettings.json, default
            // No need to update the cache here
            if (globalCache.Get(pref.Key, out TValue value))
                return value;

            bool found = GetFromDatabase(pref, out value) || GetFromEnvironment(pref, out value);
            if (!found)
                value = GetFromConfiguration(pref, pref.DefaultValue);

            // Update cache
            globalCache.Set(pref.Key, value);
            return value;
        }

        /// <summary>
        /// Gets the value of a user option.
        /// </summary>
        /// <remarks>
        /// User options are resolved in this order: cache, database, global option store
        /// </remarks>
        /// <typeparam name="TValue">Data type of option</typeparam>
        /// <param name="pref">option definition</param>
        /// <param name="userId">User ID</param>
        /// <returns>Value of option</returns>
        public TValue GetForUser<TValue>(OptionDefinition<TValue> pref, string userId)
        {
            if ((pref.Flags & OptionFlags.User) != 0 || userId == null)
            {
                // No need to update the cache here
                if (userCache.Get(new UserCacheKey(userId, pref.Key), out TValue value))
                    return value;

                bool found = GetFromDatabaseUser(pref, userId, out value);
                if (!found)
                    value = GetGlobal(pref);

                // Update cache
                userCache.Set(new UserCacheKey(userId, pref.Key), value);
                return value;
            }
            else return GetGlobal(pref);
        }

        /// <summary>
        /// Gets the value of a subscription folder option.
        /// </summary>
        /// <remarks>
        /// Folder options are inherited from parent folders to child folders. If the option is not found linked to the
        /// given folder, it will be retrieved from the parent folder. If the option is not found 
        /// </remarks>
        /// <typeparam name="TValue">Data type of option</typeparam>
        /// <param name="pref">option definition</param>
        /// <param name="folderId">Folder ID</param>
        /// <returns>Value of option</returns>
        public TValue GetForSubscriptionFolder<TValue>(OptionDefinition<TValue> pref, int folderId)
        {
            if ((pref.Flags & OptionFlags.SubscriptionFolder) != 0)
            {
                // No need to update the cache here
                if (folderCache.Get(new SubscriptionFolderCacheKey(folderId, pref.Key), out TValue value))
                    return value;

                bool found = GetFromDatabaseFolder(pref, folderId, out value);
                if (!found)
                {
                    // Get from either the parent folder or user
                    var folder = dataContext.SubscriptionFolders.Find(folderId);
                    value = (folder?.ParentId).HasValue
                        ? GetForSubscriptionFolder(pref, folder.ParentId.Value)
                        : GetForUser(pref, folder?.UserId);
                }

                // Update cache
                folderCache.Set(new SubscriptionFolderCacheKey(folderId, pref.Key), value);
                return value;
            }
            else
            {
                var userId = dataContext.SubscriptionFolders.Find(folderId)?.UserId;
                return GetForUser(pref, userId);
            }
        }

        /// <summary>
        /// Gets the value of a subscription folder option.
        /// </summary>
        /// <remarks>
        /// Folder options are inherited from parent folders to child folders. If the option is not found linked to the
        /// given folder, it will be retrieved from the parent folder. If the option is not found 
        /// </remarks>
        /// <typeparam name="TValue">Data type of option</typeparam>
        /// <param name="pref">option definition</param>
        /// <param name="folderId">Folder ID</param>
        /// <returns>Value of option</returns>
        public TValue GetForSubscription<TValue>(OptionDefinition<TValue> pref, int subId)
        {
            if ((pref.Flags & OptionFlags.Subscription) != 0)
            {
                // No need to update the cache here
                if (subCache.Get(new SubscriptionCacheKey(subId, pref.Key), out TValue value))
                    return value;

                bool found = GetFromDatabaseSubscription(pref, subId, out value);
                if (!found)
                {
                    // Get from either the parent folder or user
                    var sub = dataContext.Subscriptions.Find(subId);
                    value = (sub?.ParentFolderId).HasValue
                        ? GetForSubscriptionFolder(pref, sub.ParentFolderId.Value)
                        : GetForUser(pref, sub?.UserId);
                }

                // Update cache
                subCache.Set(new SubscriptionCacheKey(subId, pref.Key), value);
                return value;
            }
            else
            {
                // Get from either the parent folder or user
                var sub = dataContext.Subscriptions.Find(subId);
                return (sub?.ParentFolderId).HasValue
                    ? GetForSubscriptionFolder(pref, sub.ParentFolderId.Value)
                    : GetForUser(pref, sub?.UserId);
            }
        }

        private bool GetFromDatabase<TValue>(OptionDefinition<TValue> pref, out TValue value)
        {
            value = default;
            if (pref.Key == null)
                return false;

            var dbPref = dataContext.Options.Find(pref.Key);
            if (dbPref == null)
                return false;

            value = JsonSerializer.Deserialize<TValue>(dbPref.Value);
            return true;
        }

        private bool GetFromEnvironment<TValue>(OptionDefinition<TValue> pref, out TValue value)
        {
            value = default;
            if (pref.EnvironmentKey == null)
                return false;

            string envValue = Environment.GetEnvironmentVariable(pref.EnvironmentKey);
            if (envValue == null)
                return false;

            if (typeof(TValue) == typeof(string))
                value = (TValue)(object)envValue; // ugh, I know, but should work
            else
                value = JsonSerializer.Deserialize<TValue>(envValue);
            return true;
        }

        private TValue GetFromConfiguration<TValue>(OptionDefinition<TValue> pref, TValue defaultValue)
        {
            if (pref.ConfigurationKey != null)
                return configuration.GetValue(pref.ConfigurationKey, defaultValue);

            return defaultValue;
        }

        private bool GetFromDatabaseUser<TValue>(OptionDefinition<TValue> pref, string userId, out TValue value)
        {
            value = default;
            if (pref.Key == null)
                return false;

            var dbPref = dataContext.UserOptions.Find(pref.Key, userId);
            if (dbPref == null)
                return false;

            value = JsonSerializer.Deserialize<TValue>(dbPref.Value);
            return true;
        }

        private bool GetFromDatabaseFolder<TValue>(OptionDefinition<TValue> pref, int folderId, out TValue value)
        {
            value = default;
            if (pref.Key == null)
                return false;

            var dbPref = dataContext.FolderOptions.Find(pref.Key, folderId);
            if (dbPref == null)
                return false;

            value = JsonSerializer.Deserialize<TValue>(dbPref.Value);
            return true;
        }

        private bool GetFromDatabaseSubscription<TValue>(OptionDefinition<TValue> pref, int subId, out TValue value)
        {
            value = default;
            if (pref.Key == null)
                return false;

            var dbPref = dataContext.SubscriptionOptions.Find(pref.Key, subId);
            if (dbPref == null)
                return false;

            value = JsonSerializer.Deserialize<TValue>(dbPref.Value);
            return true;
        }

        public void SetGlobal<TValue>(OptionDefinition<TValue> pref, TValue value)
        {
            // Keeping track of all the dependencies would make things a lot more complicated,
            // the easier solution is to simply invalidate all the caches which depend on this one
            userCache.Invalidate();
            folderCache.Invalidate();
            subCache.Invalidate();

            globalCache.Set(pref.Key, value);
            SetInDatabase(pref, value);
        }

        public void SetForUser<TValue>(OptionDefinition<TValue> pref, string userId, TValue value)
        {
            // Keeping track of all the dependencies would make things a lot more complicated,
            // the easier solution is to simply invalidate all the caches which depend on this one
            folderCache.Invalidate();
            subCache.Invalidate();

            userCache.Set(new UserCacheKey(userId, pref.Key), value);
            SetInDatabaseUser(pref, userId, value);
        }

        public void SetForSubscriptionFolder<TValue>(OptionDefinition<TValue> pref, int folderId, TValue value)
        {
            // Keeping track of all the dependencies would make things a lot more complicated,
            // the easier solution is to simply invalidate all the caches which depend on this one
            folderCache.Invalidate();
            subCache.Invalidate();

            folderCache.Set(new SubscriptionFolderCacheKey(folderId, pref.Key), value);
            SetInDatabaseFolder(pref, folderId, value);
        }

        public void SetForSubscription<TValue>(OptionDefinition<TValue> pref, int subId, TValue value)
        {
            subCache.Set(new SubscriptionCacheKey(subId, pref.Key), value);
            SetInDatabaseSubscription(pref, subId, value);
        }

        private void SetInDatabase<TValue>(OptionDefinition<TValue> pref, TValue value)
        {
            var dbPref = dataContext.Options.Find(pref.Key);
            if (dbPref == null)
            {
                dbPref = new Option() { Key = pref.Key };
                dataContext.Options.Add(dbPref);
            }
            dbPref.Value = JsonSerializer.Serialize(value);
            dataContext.SaveChanges();
        }

        private void SetInDatabaseUser<TValue>(OptionDefinition<TValue> pref, string userId, TValue value)
        {
            var dbPref = dataContext.UserOptions.Find(pref.Key, userId);
            if (dbPref == null)
            {
                dbPref = new UserOption() { Key = pref.Key, UserId = userId };
                dataContext.UserOptions.Add(dbPref);
            }
            dbPref.Value = JsonSerializer.Serialize(value);
            dataContext.SaveChanges();
        }

        private void SetInDatabaseFolder<TValue>(OptionDefinition<TValue> pref, int folderId, TValue value)
        {
            var dbPref = dataContext.FolderOptions.Find(pref.Key, folderId);
            if (dbPref == null)
            {
                dbPref = new SubscriptionFolderOption() { Key = pref.Key, SubscriptionFolderId = folderId };
                dataContext.FolderOptions.Add(dbPref);
            }
            dbPref.Value = JsonSerializer.Serialize(value);
            dataContext.SaveChanges();
        }

        private void SetInDatabaseSubscription<TValue>(OptionDefinition<TValue> pref, int subId, TValue value)
        {
            var dbPref = dataContext.SubscriptionOptions.Find(pref.Key, subId);
            if (dbPref == null)
            {
                dbPref = new SubscriptionOption() { Key = pref.Key, SubscriptionId = subId };
                dataContext.SubscriptionOptions.Add(dbPref);
            }
            dbPref.Value = JsonSerializer.Serialize(value);
            dataContext.SaveChanges();
        }

        public bool GetForSubscriptionNoResolve<TValue>(OptionDefinition<TValue> pref, int subId, out TValue value)
        {
            return GetFromDatabaseSubscription(pref, subId, out value);
        }

        public void UnsetForSubscription<TValue>(OptionDefinition<TValue> pref, int subId)
        {
            subCache.Remove(new SubscriptionCacheKey(subId, pref.Key));
            var dbPref = dataContext.SubscriptionOptions.Find(pref.Key, subId);
            if (dbPref != null)
            {
                dataContext.SubscriptionOptions.Remove(dbPref);
                dataContext.SaveChanges();
            }
        }

        public bool GetForSubscriptionFolderNoResolve<TValue>(OptionDefinition<TValue> pref, int folderId, out TValue value)
        {
            return GetFromDatabaseFolder(pref, folderId, out value);
        }

        public void UnsetForSubscriptionFolder<TValue>(OptionDefinition<TValue> pref, int folderId)
        {
            folderCache.Remove(new SubscriptionFolderCacheKey(folderId, pref.Key));
            var dbPref = dataContext.FolderOptions.Find(pref.Key, folderId);
            if (dbPref != null)
            {
                dataContext.FolderOptions.Remove(dbPref);
                dataContext.SaveChanges();
            }
        }
    }
}
