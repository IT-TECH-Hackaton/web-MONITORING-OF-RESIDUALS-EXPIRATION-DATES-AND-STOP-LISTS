using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Monitoring.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class SecurityTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(postgres.ConnectionString);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task Anonymous_cannot_read_stock()
    {
        var response = await _client.GetAsync("/api/stock");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Waiter_cannot_receive_deliveries_or_open_kitchen()
    {
        await Authorize("waiter", "waiter123");
        var delivery = await _client.PostAsJsonAsync("/api/deliveries", new { id = Guid.NewGuid(), number = "X", items = Array.Empty<object>() });
        var kitchen = await _client.GetAsync("/api/kitchen");
        var analytics = await _client.GetAsync("/api/analytics/deficits");

        Assert.Equal(HttpStatusCode.Forbidden, delivery.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, kitchen.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, analytics.StatusCode);
    }

    [Fact]
    public async Task Kitchen_cannot_create_orders_or_deliveries()
    {
        await Authorize("kitchen", "kitchen123");
        var order = await _client.PostAsJsonAsync("/api/orders", new { id = Guid.NewGuid(), items = new[] { new { dishId = Guid.NewGuid(), portions = 1 } } });
        var delivery = await _client.PostAsJsonAsync("/api/deliveries", new { id = Guid.NewGuid(), number = "X", items = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, order.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delivery.StatusCode);
    }

    [Fact]
    public async Task Refresh_token_is_not_accepted_as_access()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { login = "manager", password = "manager123" });
        var body = await login.Content.ReadFromJsonAsync<LoginBody>(Json);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.RefreshToken);

        var response = await _client.GetAsync("/api/stock");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task Authorize(string login, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { login, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginBody>(Json);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private sealed record LoginBody(string AccessToken, string RefreshToken);
}
