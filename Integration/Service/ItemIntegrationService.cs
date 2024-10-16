using Integration.Common;
using Integration.Backend;
using System.Runtime.Caching;
using StackExchange.Redis;

namespace Integration.Service;

public sealed class ItemIntegrationService
{
    //This is a dependency that is normally fulfilled externally.
    private ItemOperationBackend ItemIntegrationBackend { get; set; } = new();

    private static readonly MemoryCache _cache = MemoryCache.Default;
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
    private IDatabase _redisCache;
    private bool runDistributed = false;

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

            bool existInCache = false;
            if (runDistributed)
                existInCache = (await _redisCache.StringGetAsync(itemContent)).HasValue;
            else
                existInCache = _cache.Contains(itemContent);

            // Check the backend to see if the content is already saved.
            if (existInCache || ItemIntegrationBackend.FindItemsWithContent(itemContent).Count != 0)
            {
                return new Result(false, $"Duplicate item received with content {itemContent}.");
            }

            //Date bilgisi ihtiyaç halinde değişir,
            //Şimdilik db'ye kayıt süresi + 5 saniye olarak set'lendi
            //ancak optimum süreye değiştirilmelidir
            if (runDistributed)
                await _redisCache.StringSetAsync(itemContent, itemContent, new TimeSpan(0, 0, seconds: 45));
            else
                _cache.Add(itemContent, true, DateTime.Now.AddSeconds(45));


            _semaphore.Release();

            var item = ItemIntegrationBackend.SaveItem(itemContent);
            return new Result(true, $"Item with content {itemContent} saved with id {item.Id}");
        });

    }

    public List<Item> GetAllItems()
    {
        return ItemIntegrationBackend.GetAllItems();
    }
}