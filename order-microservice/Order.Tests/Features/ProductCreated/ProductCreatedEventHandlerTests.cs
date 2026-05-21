using System.Globalization;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Order.Service.Contracts.Integration;
using Order.Service.Features.ProductCreated;

namespace Order.Tests.Features.ProductCreated;

public class ProductCreatedEventHandlerTests
{
    [Fact]
    public async Task Handle_WritesPriceToCacheUnderProductIdKey()
    {
        var cache = new RecordingDistributedCache();
        var handler = new ProductCreatedEventHandler(cache);

        await handler.Handle(new ProductCreatedEvent(42, "Widget", 19.95m));

        Assert.True(cache.Items.TryGetValue("42", out var entry));
        Assert.Equal("19.95", Encoding.UTF8.GetString(entry!.Value));
        Assert.Equal(TimeSpan.FromHours(24), entry.Options.SlidingExpiration);
    }

    [Fact]
    public async Task Handle_FormatsPriceUsingInvariantCulture()
    {
        var cache = new RecordingDistributedCache();
        var handler = new ProductCreatedEventHandler(cache);

        var original = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            await handler.Handle(new ProductCreatedEvent(7, "Gadget", 1234.56m));
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = original;
        }

        Assert.Equal("1234.56", Encoding.UTF8.GetString(cache.Items["7"].Value));
    }

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        public Dictionary<string, (byte[] Value, DistributedCacheEntryOptions Options)> Items { get; } = new();

        public byte[]? Get(string key) => Items.TryGetValue(key, out var v) ? v.Value : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => Items.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Items[key] = (value, options);
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}
