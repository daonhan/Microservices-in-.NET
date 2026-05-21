using System.Net.Http.Json;
using System.Text.Json;
using Basket.Service.Endpoints;
using Basket.Service.Infrastructure.Data;
using Basket.Service.Infrastructure.Data.Redis;
using Basket.Service.Infrastructure.Seeding;
using Basket.Service.Models;
using ECommerce.Shared.Qa;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;

namespace Basket.Tests.Qa;

public class BasketQaSeederTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    [Theory]
    [InlineData(nameof(QaPersonas.CustomerHappyId))]
    [InlineData(nameof(QaPersonas.CustomerDeclineId))]
    [InlineData(nameof(QaPersonas.CustomerCancelId))]
    [Trait("Category", "Integration")]
    public async Task GivenQaSeedingEnabled_WhenRequestingSeededBasket_ThenExpectedProductIsReturned(string personaKey)
    {
        var (customerId, productId, productName, productPrice, quantity) = personaKey switch
        {
            nameof(QaPersonas.CustomerHappyId) => (
                QaPersonas.CustomerHappyId,
                QaPersonas.ProductHappyId,
                QaPersonas.ProductHappyName,
                QaPersonas.ProductHappyPrice,
                QaPersonas.ProductHappyQuantity),
            nameof(QaPersonas.CustomerDeclineId) => (
                QaPersonas.CustomerDeclineId,
                QaPersonas.ProductDeclineId,
                QaPersonas.ProductDeclineName,
                QaPersonas.ProductDeclinePrice,
                QaPersonas.ProductDeclineQuantity),
            nameof(QaPersonas.CustomerCancelId) => (
                QaPersonas.CustomerCancelId,
                QaPersonas.ProductZeroStockId,
                QaPersonas.ProductZeroStockName,
                QaPersonas.ProductZeroStockPrice,
                QaPersonas.ProductZeroStockQuantity),
            _ => throw new ArgumentOutOfRangeException(nameof(personaKey))
        };

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Qa:Seed"] = "true";
        builder.Configuration["Redis:Configuration"] = _redis.GetConnectionString();

        builder.Services.AddScoped<IBasketStore, RedisBasketStore>();
        builder.Services.AddRedisCache(builder.Configuration);
        builder.Services.AddQaSeeding<RedisQaSeederHostedService>(builder.Configuration, builder.Environment);

        await using var app = builder.Build();
        app.RegisterEndpoints();

        await app.StartAsync();

        var client = app.GetTestClient();
        using var basket = await client.GetFromJsonAsync<JsonDocument>(customerId.ToString());

        Assert.NotNull(basket);
        var root = basket!.RootElement;
        Assert.Equal(customerId.ToString(), root.GetProperty("customerId").GetString());
        var product = Assert.Single(root.GetProperty("products").EnumerateArray());
        Assert.Equal(productId.ToString(System.Globalization.CultureInfo.InvariantCulture), product.GetProperty("productId").GetString());
        Assert.Equal(productName, product.GetProperty("productName").GetString());
        Assert.Equal(productPrice, product.GetProperty("productPrice").GetDecimal());
        Assert.Equal(quantity, product.GetProperty("quantity").GetInt32());
    }

    public async Task InitializeAsync() => await _redis.StartAsync();

    public async Task DisposeAsync() => await _redis.DisposeAsync();
}
