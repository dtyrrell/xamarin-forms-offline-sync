// To add offline sync support: add the NuGet package WindowsAzure.MobileServices.SQLiteStore
// to all projects in the solution and uncomment the symbol definition OFFLINE_SYNC_ENABLED
// For Xamarin.iOS, also edit AppDelegate.cs and uncomment the call to SQLitePCL.CurrentPlatform.Init()
// For more information, see: http://go.microsoft.com/fwlink/?LinkId=620342 
#define OFFLINE_SYNC_ENABLED

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.MobileServices;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;


#if OFFLINE_SYNC_ENABLED
using Microsoft.WindowsAzure.MobileServices.SQLiteStore;
using Microsoft.WindowsAzure.MobileServices.Sync;
#endif

namespace XamarinFormsOffline
{
    public partial class TodoItemManager
    {
        static TodoItemManager defaultInstance = new TodoItemManager();
        MobileServiceClient client;

        #if OFFLINE_SYNC_ENABLED
        IMobileServiceSyncTable<TodoItem> todoTable;
        #else
        IMobileServiceTable<TodoItem> todoTable;
#endif

        private TodoItemManager()
        {
            this.client = new MobileServiceClient("http://yoursitename.azurewebsites.net");

#if OFFLINE_SYNC_ENABLED
            var store = new MobileServiceSQLiteStore("localstore.db");
            store.DefineTable<TodoItem>();

            //Initializes the SyncContext using the default IMobileServiceSyncHandler.
            this.client.SyncContext.InitializeAsync(store);

            this.todoTable = client.GetSyncTable<TodoItem>();
#else
            this.todoTable = client.GetTable<TodoItem>();
#endif
        }

        public static TodoItemManager DefaultManager
        {
            get
            {
                return defaultInstance;
            }
            private set
            {
                defaultInstance = value;
            }
        }

        public MobileServiceClient CurrentClient
        {
            get { return client; }
        }

        public bool IsOfflineEnabled
        {
            get { return todoTable is Microsoft.WindowsAzure.MobileServices.Sync.IMobileServiceSyncTable<TodoItem>; }
        }

        public async Task<ObservableCollection<TodoItem>> GetTodoItemsAsync(bool syncItems = false)
        {
            try
            {
#if OFFLINE_SYNC_ENABLED
                if (syncItems)
                {
                    await this.SyncAsync();
                }
#endif
                IEnumerable<TodoItem> items = await todoTable
                    .Where(todoItem => !todoItem.Done)
                    .OrderBy(todoItem => todoItem.UpdatedAt)
                    .ToEnumerableAsync();

                return new ObservableCollection<TodoItem>(items);
            }
            catch (MobileServiceInvalidOperationException msioe)
            {
                Debug.WriteLine(@"Invalid sync operation: {0}", msioe.Message);
            }
            catch (Exception e)
            {
                Debug.WriteLine(@"Sync error: {0}", e.Message);
            }
            return null;
        }

        public async Task SaveTaskAsync(TodoItem item)
        {
            if (item.Id == null)
            {
                await todoTable.InsertAsync(item);
            }
            else
            {
                await todoTable.UpdateAsync(item);
            }
        }

        #if OFFLINE_SYNC_ENABLED
        public async Task SyncAsync()
        {
            try
            {
                await this.client.SyncContext.PushAsync();

                await this.todoTable.PullAsync(
                    //The first parameter is a query name that is used internally by the client SDK to implement incremental sync.
                    //Use a different query name for each unique query in your program
                    "allTodoItems",
                    this.todoTable.CreateQuery());
            }
            catch (MobileServicePushFailedException exc)
            {
                if (exc.PushResult != null)
                {
                    await ResolveConflictsAsync(exc.PushResult.Errors);
                }
            }
        }

        private async Task ResolveConflictsAsync(ReadOnlyCollection<MobileServiceTableOperationError> syncErrors)
        {
            foreach (var error in syncErrors)
            {
                Debug.WriteLine($"Conflict during update: Item: {error.Item}");

                var serverItem = error.Result.ToObject<TodoItem>();
                var localItem = error.Item.ToObject<TodoItem>();

                if (serverItem.Done == localItem.Done && serverItem.Name == localItem.Name)
                {
                    // items are same so we can ignore the conflict
                    await error.CancelAndDiscardItemAsync();
                }
                else
                {
                    var userAction = await App.Current.MainPage.DisplayAlert("Conflict", $"Local version: {localItem}\nServer version: {serverItem}", "Use server", "Use client"); 

                    if (userAction)
                    {
                        Debug.WriteLine("Use server!");
                        await error.CancelAndUpdateItemAsync(error.Result);
                    }
                    else
                    {
                        localItem.Version = serverItem.Version;
                        await error.UpdateOperationAsync(JObject.FromObject(localItem));
                    }    
                }
            }
           
        }
        #endif
    }
}
