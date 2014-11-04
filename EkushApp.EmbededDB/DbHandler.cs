﻿using Raven.Abstractions.Data;
using Raven.Abstractions.Linq;
using Raven.Client;
using Raven.Client.Embedded;
using Raven.Client.Indexes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EkushApp.Model;
using EkushApp.Logging;
using EkushApp.Utility.Extensions;
using System.IO;
using Raven.Json.Linq;
using Raven.Abstractions.Exceptions;
using System.Collections.Concurrent;
using Raven.Client.UniqueConstraints;
using System.Threading;

namespace EkushApp.EmbededDB
{
    public class DbHandler : IDisposable
    {
        #region Declaration(s)
        public static string DatabasePath { get; set; }
        private readonly object _lockObject = new object();
        private SemaphoreSlim _syncLock = new SemaphoreSlim(1);
        #endregion

        #region Property(s)
        private static DbHandler _instance;
        public static DbHandler Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new DbHandler();
                }
                return _instance;
            }
        }
        private Lazy<IDocumentStore> DocStore = new Lazy<IDocumentStore>(() =>
        {
            var documentStore = new EmbeddableDocumentStore()
            {
                DataDirectory = DatabasePath
            };

            documentStore.RegisterListener(new UniqueConstraintsStoreListener());
            documentStore.Configuration.ResetIndexOnUncleanShutdown = true;
            documentStore.Configuration.MaxPageSize = 10000;
            documentStore.Configuration.MaxNumberOfItemsToIndexInSingleBatch = 1024 * 1024;
            documentStore.Configuration.MaxNumberOfItemsToPreFetchForIndexing = 1024 * 1024;
            documentStore.Configuration.MaxNumberOfItemsToReduceInSingleBatch = 1024 * 1024;
            documentStore.Configuration.NewIndexInMemoryMaxBytes = 128;
            documentStore.Configuration.InitialNumberOfItemsToIndexInSingleBatch = 1024;
            documentStore.Initialize();

            return documentStore;
        });
        private IDocumentStore DocumentStore
        {
            get { return DocStore.Value; }
        }
        #endregion

        #region Constructor(s)
        public DbHandler()
        {
            IndexCreation.CreateIndexes(typeof(AppUserMapReduceIndex).Assembly, DocumentStore);
            IndexCreation.CreateIndexes(typeof(HardwareMapIndex).Assembly, DocumentStore);
            IndexCreation.CreateIndexes(typeof(SupplierMapReduceIndex).Assembly, DocumentStore);
            IndexCreation.CreateIndexes(typeof(UserMapReduceIndex).Assembly, DocumentStore);
        }
        ~DbHandler()
        {
            Dispose(false);
        }
        #endregion

        #region AppUser Operation(s)
        public async Task<bool> SaveAppUserData(AppUser user)
        {
            try
            {
                using (var session = DocumentStore.OpenAsyncSession())
                {
                    await session.Advanced.DocumentStore.AsyncDatabaseCommands.DeleteByIndexAsync("AppUserMapReduceIndex",
                                                    new IndexQuery
                                                    {
                                                        Query = "Username:" + user.Username
                                                    }, false);
                    await session.StoreAsync(user);
                    await session.SaveChangesAsync();
                    return true;
                }
            }
            catch (Exception x)
            {
                Log.Error("Error when save user data.", x);
            }
            return false;
        }
        public async Task<bool> AuthenticateUser(string username, string password, Action<AppUser> appUser)
        {
            try
            {
                using (var session = DocumentStore.OpenAsyncSession())
                {
                    var response = await session.Advanced.AsyncLuceneQuery<AppUser>("AppUserMapReduceIndex").Where("Username:" + username + " AND Password:" + password).WaitForNonStaleResultsAsOfLastWrite().ToListAsync();
                    if (response != null && response.Count > 0)
                    {
                        appUser(response.First());
                        return true;
                    }
                }
            }
            catch (Exception x)
            {
                Log.Error("Error when authenticate user.", x);
            }
            return false;
        }
        public async Task<List<AppUser>> GetUsers()
        {
            List<AppUser> users = new List<AppUser>();
            try
            {
                using (var session = DocumentStore.OpenAsyncSession())
                {
                    var response = await session.Advanced.AsyncLuceneQuery<AppUser>("AppUserMapReduceIndex").WaitForNonStaleResultsAsOfLastWrite().ToListAsync();
                    users.AddRange(response);
                }
            }
            catch (Exception x)
            {
                Log.Error("Error when authenticate user.", x);
            }
            return users;
        }
        public async Task<List<AppUser>> GetAppUser()
        {
            List<AppUser> users = new List<AppUser>();
            try
            {
                using (var session = DocumentStore.OpenAsyncSession())
                {
                    var response = await session.Advanced.AsyncLuceneQuery<AppUser>("AppUserMapReduceIndex").WaitForNonStaleResultsAsOfLastWrite().ToListAsync();
                    users.AddRange(response);
                }
            }
            catch (Exception x)
            {
                Log.Error("Error when authenticate user.", x);
            }
            return users;
        }
        public async Task DeleteAppUser(AppUser user)
        {
            try
            {
                using (var session = DocumentStore.OpenAsyncSession())
                {
                    var delOperation = await session.Advanced.DocumentStore.AsyncDatabaseCommands.DeleteByIndexAsync("AppUserMapReduceIndex",
                                                    new IndexQuery
                                                    {
                                                        Query = "Username: " + user.Username
                                                    }, false);
                    await delOperation.WaitForCompletionAsync();
                }
            }
            catch (Exception x)
            {
                Log.Error("Error when delete hardware.", x);
            }
        }
        #endregion

        #region Hardware
        public async Task SaveHardware(Hardware hardware)
        {
            try
            {
                using (var session = DocumentStore.OpenAsyncSession())
                {
                    session.Advanced.UseOptimisticConcurrency = true;
                    await session.Advanced.DocumentStore.AsyncDatabaseCommands.DeleteByIndexAsync("HardwareMapIndex",
                                                    new IndexQuery
                                                    {
                                                        Query = "SerialNo: " + hardware.SerialNo
                                                    }, false);
                    var maxIds = await session.Advanced.AsyncLuceneQuery<Hardware>("HardwareMapIndex")
                        .WaitForNonStaleResultsAsOfLastWrite().OrderByDescending(p => p.SerialNo).ToListAsync();
                    if (maxIds != null && maxIds.Count > 0)
                    {
                        var maxId = maxIds.Select(id => (long)id.SerialNo).Max();
                        hardware.SerialNo = maxId + 1;
                    }
                    else
                    {
                        hardware.SerialNo = 1;
                    }
                    await session.StoreAsync(hardware);
                    await session.SaveChangesAsync();
                }
            }
            catch (Exception x)
            {
                Log.Error("Error when save hardware.", x);
            }
        }
        public async Task<List<Hardware>> GetHardwareCollection()
        {
            List<Hardware> hardwareBag = new List<Hardware>();
            try
            {
                using (var session = DocumentStore.OpenAsyncSession())
                {
                    var hardWares = await session.Advanced.AsyncLuceneQuery<Hardware>("HardwareMapIndex")
                        .WaitForNonStaleResultsAsOfLastWrite().OrderBy(p => p.SerialNo).ToListAsync();
                    if (hardWares != null && hardWares.Count > 0)
                    {
                        hardwareBag.AddRange(hardWares);
                    }
                }
            }
            catch (Exception x)
            {
                Log.Error("Error when save hardware.", x);
            }
            return hardwareBag;
        }
        public async Task DeleteHardware(Hardware hardware)
        {
            try
            {
                using (var session = DocumentStore.OpenAsyncSession())
                {
                    var delOperation = await session.Advanced.DocumentStore.AsyncDatabaseCommands.DeleteByIndexAsync("HardwareMapIndex",
                                                    new IndexQuery
                                                    {
                                                        Query = "SerialNo: " + hardware.SerialNo
                                                    }, false);
                    await delOperation.WaitForCompletionAsync();
                }
            }
            catch (Exception x)
            {
                Log.Error("Error when delete hardware.", x);
            }
        }
        #endregion

        #region User
        public async Task SaveUser(User user)
        {
            try
            {
                using (var session = DocumentStore.OpenAsyncSession())
                {
                    await session.Advanced.DocumentStore.AsyncDatabaseCommands.DeleteByIndexAsync("UserMapReduceIndex",
                                                    new IndexQuery
                                                    {
                                                        Query = "Id: " + user.Id
                                                    }, false);
                    var maxIds = await session.Advanced.AsyncLuceneQuery<User>("UserMapReduceIndex")
                        .WaitForNonStaleResultsAsOfLastWrite().OrderByDescending(p => p.Id).ToListAsync();
                    if (maxIds != null && maxIds.Count > 0)
                    {
                        var maxId = maxIds.Select(id => (long)id.Id).Max();
                        user.Id = maxId + 1;
                    }
                    else
                    {
                        user.Id = 1;
                    }
                    await session.StoreAsync(user);
                    await session.SaveChangesAsync();
                }
            }
            catch (Exception x)
            {
                Log.Error("Error when save hardware.", x);
            }
        }
        public async Task<List<User>> GetUserCollection()
        {
            List<User> hardwareBag = new List<User>();
            try
            {
                using (var session = DocumentStore.OpenAsyncSession())
                {
                    var hardWares = await session.Advanced.AsyncLuceneQuery<User>("UserMapReduceIndex")
                        .WaitForNonStaleResultsAsOfLastWrite().ToListAsync();
                    if (hardWares != null && hardWares.Count > 0)
                    {
                        hardwareBag.AddRange(hardWares);
                    }
                }
            }
            catch (Exception x)
            {
                Log.Error("Error when save hardware.", x);
            }
            return hardwareBag;
        }
        public async Task DeleteUser(User user)
        {
            try
            {
                using (var session = DocumentStore.OpenAsyncSession())
                {
                    var delOperation = await session.Advanced.DocumentStore.AsyncDatabaseCommands.DeleteByIndexAsync("UserMapReduceIndex",
                                                    new IndexQuery
                                                    {
                                                        Query = "Id: " + user.Id
                                                    }, false);
                    await delOperation.WaitForCompletionAsync();
                }
            }
            catch (Exception x)
            {
                Log.Error("Error when delete hardware.", x);
            }
        }
        #endregion

        #region Supplier
        public async Task SaveSupplier(Supplier supplier)
        {
            try
            {
                using (var session = DocumentStore.OpenAsyncSession())
                {
                    await session.StoreAsync(supplier);
                    await session.SaveChangesAsync();
                }
            }
            catch (Exception x)
            {
                Log.Error("Error when save hardware.", x);
            }
        }
        public async Task<List<Supplier>> GetSupplierCollection()
        {
            List<Supplier> hardwareBag = new List<Supplier>();
            try
            {
                using (var session = DocumentStore.OpenAsyncSession())
                {
                    var hardWares = await session.Advanced.AsyncLuceneQuery<Supplier>("SupplierMapReduceIndex")
                        .WaitForNonStaleResultsAsOfLastWrite().ToListAsync();
                    if (hardWares != null && hardWares.Count > 0)
                    {
                        hardwareBag.AddRange(hardWares);
                    }
                }
            }
            catch (Exception x)
            {
                Log.Error("Error when save hardware.", x);
            }
            return hardwareBag;
        }
        public async Task DeleteSupplier(Supplier supplier)
        {
            try
            {
                using (var session = DocumentStore.OpenAsyncSession())
                {
                    var delOperation = await session.Advanced.DocumentStore.AsyncDatabaseCommands.DeleteByIndexAsync("SupplierMapReduceIndex",
                                                    new IndexQuery
                                                    {
                                                        Query = "Id: " + supplier.Id
                                                    }, false);
                    await delOperation.WaitForCompletionAsync();
                }
            }
            catch (Exception x)
            {
                Log.Error("Error when delete hardware.", x);
            }
        }
        #endregion

        #region Method(s)
        public static void ShutDownDatabase()
        {
            if (_instance != null)
            {
                _instance.DocumentStore.Dispose();
                _instance.Dispose();
                _instance = null;
            }
        }
        #endregion

        #region IDisposeable
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
        }
        #endregion
    }
}
