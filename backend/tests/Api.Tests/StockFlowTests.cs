using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Services;
using Testcontainers.PostgreSql;

namespace Monitoring.Api.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(ApiCollection.Name)]
public sealed class ApiCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "api";
}

[Collection(ApiCollection.Name)]
public sealed class StockFlowTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(postgres.ConnectionString);
        _client = _factory.CreateClient();
        await Authorize();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task Parallel_orders_cannot_oversell_the_last_portion()
    {
        var productId = Guid.NewGuid();
        var dishId = await SeedPortion(productId, 100, 100, _factory.Clock.UtcNow.AddDays(2));
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var first = PostOrder(firstId, dishId);
        var second = PostOrder(secondId, dishId);
        var responses = await Task.WhenAll(first, second);
        var codes = responses.Select(response => response.StatusCode).OrderBy(code => code).ToArray();

        Assert.Equal(HttpStatusCode.Created, codes[0]);
        Assert.Equal(HttpStatusCode.Conflict, codes[1]);

        var stock = await _client.GetFromJsonAsync<List<StockItem>>("/api/stock", Json);
        var row = stock!.Single(item => item.ProductId == productId);
        Assert.Equal(0, row.Available);
    }

    [Fact]
    public async Task Repeated_order_id_writes_off_once()
    {
        var productId = Guid.NewGuid();
        var dishId = await SeedPortion(productId, 200, 100, _factory.Clock.UtcNow.AddDays(2));
        var orderId = Guid.NewGuid();

        var created = await PostOrder(orderId, dishId);
        var replay = await PostOrder(orderId, dishId);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        var stock = await _client.GetFromJsonAsync<List<StockItem>>("/api/stock", Json);
        Assert.Equal(100, stock!.Single(item => item.ProductId == productId).Available);
    }

    [Fact]
    public async Task Expiry_removes_the_batch_and_stops_the_dish()
    {
        var productId = Guid.NewGuid();
        var expiresAt = _factory.Clock.UtcNow.AddHours(1);
        var dishId = await SeedPortion(productId, 180, 180, expiresAt);

        var before = await _client.GetFromJsonAsync<List<StopEntry>>("/api/stop-list", Json);
        Assert.False(before!.Single(entry => entry.DishId == dishId).IsStopped);

        _factory.Clock.UtcNow = expiresAt.AddMinutes(1);
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ExpiryProcessor>().RunOnceAsync(CancellationToken.None);

        var after = await _client.GetFromJsonAsync<List<StopEntry>>("/api/stop-list", Json);
        var entry = after!.Single(item => item.DishId == dishId);
        Assert.False(entry.IsStopped);
        Assert.True(entry.IsCandidate);
        Assert.Contains("не хватает", entry.CandidateReason ?? "");

        var batches = await _client.GetFromJsonAsync<List<BatchItem>>($"/api/stock/{productId}/batches", Json);
        var expired = Assert.Single(batches!);
        Assert.Equal("Expired", expired.Status);
        Assert.Equal(0, expired.QuantityRemaining);
    }

    private async Task<Guid> SeedPortion(Guid productId, decimal stock, decimal perPortion, DateTime expiresAt)
    {
        var deliveryId = Guid.NewGuid();
        var delivery = await _client.PostAsJsonAsync("/api/deliveries", new
        {
            id = deliveryId,
            number = $"Т-{deliveryId.ToString()[..8]}",
            items = new[]
            {
                new { productId, name = "Тестовый продукт", unit = "G", quantity = stock, expiresAt },
            },
        });
        delivery.EnsureSuccessStatusCode();

        var dish = await _client.PostAsJsonAsync("/api/dishes", new
        {
            name = $"Блюдо {Guid.NewGuid():N}"[..16],
            ingredients = new[] { new { productId, quantity = perPortion } },
        });
        dish.EnsureSuccessStatusCode();
        var body = await dish.Content.ReadFromJsonAsync<DishItem>(Json);
        return body!.Id;
    }

    private Task<HttpResponseMessage> PostOrder(Guid orderId, Guid dishId) =>
        _client.PostAsJsonAsync("/api/orders", new
        {
            id = orderId,
            items = new[] { new { dishId, portions = 1 } },
        });

    private async Task Authorize()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { login = "manager", password = "manager123" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginItem>(Json);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private sealed record LoginItem(string AccessToken);
    private sealed record StockItem(Guid ProductId, decimal Available);
    private sealed record StopEntry(Guid DishId, bool IsStopped, bool IsCandidate, string? Reason, string? CandidateReason);
    private sealed record BatchItem(string Status, decimal QuantityRemaining);
    private sealed record DishItem(Guid Id);
}

public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public MutableClock Clock { get; } = new() { UtcNow = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc) };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", connectionString);
        builder.UseSetting("Seed:DemoData", "false");
        builder.UseSetting("Expiry:Enabled", "false");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = connectionString,
                ["Seed:DemoData"] = "false",
                ["Expiry:Enabled"] = "false",
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }
}
