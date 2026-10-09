using System.Security.Claims;
using Monitoring.Api.Auth;
using Monitoring.Core;
using Monitoring.Infrastructure.Services;

namespace Monitoring.Api;

public static class EndpointExtensions
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapPost("/login", async (LoginRequest request, AuthService auth, JwtTokenService tokens, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password)
                || request.Login.Length > 64 || request.Password.Length > 128)
            {
                return Results.BadRequest(new { message = "Укажите логин и пароль." });
            }

            var user = await auth.AuthenticateAsync(request.Login.Trim(), request.Password, cancellationToken);
            return user is null
                ? Results.Json(new { message = "Неверный логин или пароль" }, statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(ToResponse(tokens.Issue(user)));
        });
        group.MapPost("/refresh", async (RefreshRequest request, AuthService auth, JwtTokenService tokens, CancellationToken cancellationToken) =>
        {
            var parsed = tokens.ReadRefresh(request.RefreshToken);
            if (parsed is null)
            {
                return Results.Json(new { message = "Сессия истекла" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await auth.FindAsync(parsed.Id, cancellationToken);
            return user is null
                ? Results.Json(new { message = "Сессия истекла" }, statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(ToResponse(tokens.Issue(user)));
        });
    }

    public static void MapCatalogEndpoints(this WebApplication app)
    {
        var products = app.MapGroup("/api/products").RequireAuthorization("Staff");
        products.MapGet("/", async (CatalogService catalog, CancellationToken cancellationToken) =>
            Results.Ok(await catalog.ProductsAsync(cancellationToken)));
        products.MapPost("/", async (CreateProductRequest request, CatalogService catalog, CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<Unit>(request.Unit, true, out var unit))
            {
                return Results.BadRequest(new { message = "Единица измерения: G, Ml или Pcs." });
            }

            var result = await catalog.CreateProductAsync(new CreateProductCommand(request.Name, unit), cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new { message = result.Error });
        }).RequireAuthorization("Manager");

        var dishes = app.MapGroup("/api/dishes").RequireAuthorization("Staff");
        dishes.MapGet("/", async (CatalogService catalog, CancellationToken cancellationToken) =>
            Results.Ok(await catalog.DishesAsync(cancellationToken)));
        dishes.MapPost("/", async (DishRequest request, CatalogService catalog, CancellationToken cancellationToken) =>
            await SaveDish(catalog, null, request, cancellationToken)).RequireAuthorization("Manager");
        dishes.MapPut("/{id:guid}", async (Guid id, DishRequest request, CatalogService catalog, CancellationToken cancellationToken) =>
            await SaveDish(catalog, id, request, cancellationToken)).RequireAuthorization("Manager");
    }

    public static void MapDeliveryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/deliveries").RequireAuthorization("Manager");
        group.MapGet("/", async (DeliveryService deliveries, CancellationToken cancellationToken) =>
            Results.Ok(await deliveries.ListAsync(cancellationToken)));
        group.MapGet("/{id:guid}", async (Guid id, DeliveryService deliveries, CancellationToken cancellationToken) =>
        {
            var delivery = await deliveries.GetAsync(id, cancellationToken);
            return delivery is null ? Results.NotFound(new { message = "Поставка не найдена" }) : Results.Ok(delivery);
        });
        group.MapGet("/qr-sample", (bool? valid, QrIntakeService intake) =>
            Results.Ok(new { payload = intake.Sample(valid ?? true) }));
        group.MapPost("/scan", async (QrScanRequest request, QrIntakeService intake, CancellationToken cancellationToken) =>
        {
            var result = await intake.ScanAsync(request.Payload, cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new { message = result.Error });
        });
        group.MapPost("/", async (DeliveryRequest request, DeliveryService deliveries, CancellationToken cancellationToken) =>
        {
            if (request.Items is null || request.Items.Count == 0)
            {
                return Results.BadRequest(new { message = "В поставке нет продуктов." });
            }

            var lines = new List<DeliveryLineCommand>();
            foreach (var item in request.Items)
            {
                if (!Enum.TryParse<Unit>(item.Unit, true, out var unit))
                {
                    return Results.BadRequest(new { message = "Единица измерения: G, Ml или Pcs." });
                }

                lines.Add(new DeliveryLineCommand(item.ProductId, item.Name, unit, item.Quantity, item.ExpiresAt, item.ShelfLifeHours));
            }

            var result = await deliveries.ReceiveAsync(
                new ReceiveDeliveryCommand(request.Id, request.Number, request.CreatedAt, lines),
                cancellationToken);
            if (result.Error is not null || result.Value is null)
            {
                return Results.BadRequest(new { message = result.Error });
            }

            return result.Value.Created
                ? Results.Created($"/api/deliveries/{result.Value.Delivery.Id}", result.Value.Delivery)
                : Results.Ok(result.Value.Delivery);
        });
    }

    public static void MapStockEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization("Staff");
        group.MapGet("/stock", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.StockAsync(cancellationToken)));
        group.MapGet("/stock/{productId:guid}/batches", async (Guid productId, StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.BatchesAsync(productId, cancellationToken)));
        group.MapGet("/stop-list", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.StopListAsync(cancellationToken)));
        group.MapGet("/hot-offers", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.HotOffersAsync(cancellationToken)));
        group.MapGet("/dashboard", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.DashboardAsync(cancellationToken))).RequireAuthorization("Manager");
    }

    public static void MapOrderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/orders").RequireAuthorization("Staff");
        group.MapGet("/", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.OrdersAsync(cancellationToken)));
        group.MapGet("/{id:guid}", async (Guid id, StockReadModel stock, CancellationToken cancellationToken) =>
        {
            var order = await stock.OrderAsync(id, cancellationToken);
            return order is null ? Results.NotFound(new { message = "Заказ не найден" }) : Results.Ok(order);
        });
        group.MapPost("/", async (OrderRequest request, OrderService orders, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            if (request.Items is null || request.Items.Count == 0)
            {
                return Results.BadRequest(new { message = "В заказе нет блюд." });
            }

            var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var placement = await orders.PlaceAsync(
                new PlaceOrderCommand(
                    request.Id,
                    userId,
                    request.ConnectionId,
                    request.Items.Select(item => new OrderLineCommand(item.DishId, item.Portions)).ToList()),
                cancellationToken);
            return ToOrderResult(placement);
        }).RequireAuthorization("Orders");
    }

    public static void MapKitchenEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/kitchen").RequireAuthorization("Kitchen");
        group.MapGet("/", async (KitchenService kitchen, CancellationToken cancellationToken) =>
            Results.Ok(await kitchen.BoardAsync(cancellationToken)));
        group.MapPost("/orders/{orderId:guid}/items/{itemId:guid}", async (
            Guid orderId,
            Guid itemId,
            KitchenDishMark request,
            KitchenService kitchen,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<KitchenStatus>(request.Status, true, out var status))
            {
                return Results.BadRequest(new { message = "Статус: Queued, Cooking или Ready." });
            }

            var result = await kitchen.MarkDishAsync(orderId, itemId, status, request.Note, cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new { message = result.Error });
        });
        group.MapPost("/batches/{batchId:guid}", async (
            Guid batchId,
            KitchenBatchMark request,
            KitchenService kitchen,
            CancellationToken cancellationToken) =>
        {
            var result = await kitchen.MarkBatchAsync(batchId, request.Opened, request.Note, request.WriteOffQuantity, cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new { message = result.Error });
        });
    }

    public static void MapAnalyticsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/analytics").RequireAuthorization("Manager");
        group.MapGet("/write-offs", async (DateTime? from, DateTime? to, StockReadModel stock, CancellationToken cancellationToken) =>
        {
            var end = to ?? DateTime.UtcNow.AddDays(1);
            var start = from ?? end.AddDays(-30);
            return Results.Ok(await stock.WriteOffsAsync(DateTime.SpecifyKind(start, DateTimeKind.Utc), DateTime.SpecifyKind(end, DateTimeKind.Utc), cancellationToken));
        });
        group.MapGet("/deficits", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.DeficitsAsync(cancellationToken)));
        group.MapGet("/weekly-forecast", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.WeeklyForecastAsync(cancellationToken)));
        group.MapPost("/purchase-request", async (PurchaseRequest request, QrIntakeService intake, CancellationToken cancellationToken) =>
        {
            var result = await intake.PurchaseRequestAsync(request.ProductIds, cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new { message = result.Error });
        });
    }

    public static void MapSimulatorEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/simulator").RequireAuthorization("Manager");
        group.MapPost("/delivery", async (SimulatorService simulator, CancellationToken cancellationToken) =>
        {
            var result = await simulator.DeliveryAsync(cancellationToken);
            return result.Error is null ? Results.Ok(result.Value!.Delivery) : Results.BadRequest(new { message = result.Error });
        });
        group.MapPost("/order", async (SimulatorService simulator, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return ToOrderResult(await simulator.OrderAsync(userId, cancellationToken));
        });
    }

    private static async Task<IResult> SaveDish(CatalogService catalog, Guid? id, DishRequest request, CancellationToken cancellationToken)
    {
        var command = new UpsertDishCommand(
            request.Name,
            request.Ingredients.Select(line => new DishIngredientCommand(line.ProductId, line.Quantity)).ToList());
        var result = id is null
            ? await catalog.CreateDishAsync(command, cancellationToken)
            : await catalog.UpdateDishAsync(id.Value, command, cancellationToken);
        return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new { message = result.Error });
    }

    private static IResult ToOrderResult(OrderPlacement placement)
    {
        if (placement.Error is not null)
        {
            return Results.BadRequest(new { message = placement.Error });
        }

        if (placement.IsRejected)
        {
            return Results.Json(placement.Order, statusCode: StatusCodes.Status409Conflict);
        }

        return placement.Created
            ? Results.Created($"/api/orders/{placement.Order!.Id}", placement.Order)
            : Results.Ok(placement.Order);
    }

    private static object ToResponse(AuthTokens tokens) => new
    {
        accessToken = tokens.AccessToken,
        refreshToken = tokens.RefreshToken,
        role = tokens.User.Role.ToString(),
        login = tokens.User.Login,
        userId = tokens.User.Id,
    };
}

public sealed record LoginRequest(string Login, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record CreateProductRequest(string Name, string Unit);
public sealed record DishIngredientRequest(Guid ProductId, decimal Quantity);
public sealed record DishRequest(string Name, IReadOnlyList<DishIngredientRequest> Ingredients);
public sealed record DeliveryItemRequest(Guid ProductId, string Name, string Unit, decimal Quantity, DateTime? ExpiresAt, double? ShelfLifeHours);
public sealed record DeliveryRequest(Guid Id, string Number, DateTime? CreatedAt, IReadOnlyList<DeliveryItemRequest> Items);
public sealed record QrScanRequest(string Payload);
public sealed record PurchaseRequest(IReadOnlyList<Guid>? ProductIds);
public sealed record KitchenDishMark(string Status, string? Note);
public sealed record KitchenBatchMark(bool Opened, string? Note, decimal? WriteOffQuantity);
public sealed record OrderItemRequest(Guid DishId, int Portions);
public sealed record OrderRequest(Guid Id, string? ConnectionId, IReadOnlyList<OrderItemRequest> Items);
