using Integration.Backend;
using Integration.Common;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Integration.Service
{
    public sealed class ItemIntegrationDistributedService
    {
        //This is a dependency that is normally fulfilled externally.
        private ItemOperationBackend ItemIntegrationBackend { get; set; } = new();
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private IDatabase _redisCache;

        // This is called externally and can be called multithreaded, in parallel.
        // More than one item with the same content should not be saved. However,
        // calling this with different contents at the same time is OK, and should
        // be allowed for performance reasons.
        public async Task<Result> SaveItem(string itemContent)
        {
            return
            await Task<Result>.Run(async () =>
            {
                await _semaphore.WaitAsync();

                string existInCache = await _redisCache.StringGetAsync(itemContent);

                // Check the backend to see if the content is already saved.
                if (!string.IsNullOrEmpty(existInCache) || ItemIntegrationBackend.FindItemsWithContent(itemContent).Count != 0)
                {
                    return new Result(false, $"Duplicate item received with content {itemContent}.");
                }

                _semaphore.Release();

                //Date bilgisi ihtiyaç halinde değişir,
                //Şimdilik db'ye kayıt süresi + 5 saniye olarak set'lendi
                //ancak optimum süreye değiştirilmelidir
                await _redisCache.StringSetAsync(itemContent, itemContent, new TimeSpan(0, 0, seconds: 45));

                var item = ItemIntegrationBackend.SaveItem(itemContent);
                return new Result(true, $"Item with content {itemContent} saved with id {item.Id}");
            });
        }

        public List<Item> GetAllItems()
        {
            return ItemIntegrationBackend.GetAllItems();
        }
    }
}
