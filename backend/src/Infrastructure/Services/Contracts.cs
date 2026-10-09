using Monitoring.Core;

namespace Monitoring.Infrastructure.Services;

public sealed record AuthenticatedUser(Guid Id, string Login, UserRole Role);

public sealed record ReceiveDeliveryCommand(
    Guid Id,
    string Number,
    DateTime? CreatedAt,
    IReadOnlyList<DeliveryLineCommand> Items,
    string? Supplier = null);

public sealed record QualityCertificateCommand(string Number, string Issuer, DateTime IssuedAt, DateTime ExpiresAt);

public sealed record DeliveryLineCommand(
    Guid ProductId,
    string Name,
    Unit Unit,
    decimal Quantity,
    DateTime? ExpiresAt,
    double? ShelfLifeHours,
    QualityCertificateCommand? Certificate = null);

public sealed record PlaceOrderCommand(
    Guid Id,
    Guid CreatedBy,
    string? ConnectionId,
    IReadOnlyList<OrderLineCommand> Items);

public sealed record OrderLineCommand(Guid DishId, int Portions);

public sealed record UpsertDishCommand(
    string Name,
    IReadOnlyList<DishIngredientCommand> Ingredients);

public sealed record DishIngredientCommand(Guid ProductId, decimal Quantity);

public sealed record CreateProductCommand(string Name, Unit Unit);

public sealed record BatchView(
    Guid Id,
    Guid DeliveryId,
    string DeliveryNumber,
    Guid ProductId,
    string ProductName,
    Unit Unit,
    decimal QuantityInitial,
    decimal QuantityRemaining,
    DateTime ReceivedAt,
    DateTime ExpiresAt,
    double HoursLeft,
    BatchStatus Status,
    Freshness Freshness,
    string? CertificateNumber = null,
    string? CertificateIssuer = null,
    DateTime? CertificateExpiresAt = null);

public sealed record DeliveryView(
    Guid Id,
    string Number,
    DateTime CreatedAt,
    IReadOnlyList<BatchView> Batches,
    string? Supplier = null);

public sealed record QrLineVerdict(string ProductName, string? CertificateNumber, bool Ok, string? Reason);

public sealed record QrIntakeResult(
    bool Accepted,
    string Summary,
    IReadOnlyList<QrLineVerdict> Lines,
    DeliveryView? Delivery);

public sealed record DeliveryReceipt(DeliveryView Delivery, bool Created);

public sealed record ShortageView(
    Guid ProductId,
    string ProductName,
    decimal Required,
    decimal Available,
    decimal Deficit);

public sealed record OrderLineView(
    Guid Id,
    Guid DishId,
    string DishName,
    int Portions,
    KitchenStatus KitchenStatus,
    string? KitchenNote);

public sealed record OrderView(
    Guid Id,
    DateTime CreatedAt,
    string CreatedByLogin,
    OrderStatus Status,
    string? RejectionReason,
    IReadOnlyList<OrderLineView> Items,
    IReadOnlyList<ShortageView> Shortages);

public sealed record OrderPlacement(OrderView? Order, string? Error, bool Created, bool IsRejected)
{
    public static OrderPlacement Invalid(string error) => new(null, error, false, false);

    public static OrderPlacement Accepted(OrderView order, bool created) => new(order, null, created, false);

    public static OrderPlacement Rejected(OrderView order, bool created) => new(order, null, created, true);
}

public sealed record StockItemView(
    Guid ProductId,
    string Name,
    Unit Unit,
    decimal Available,
    DateTime? NearestExpiry,
    double? HoursLeft,
    Freshness Freshness);

public sealed record ProductView(Guid Id, string Name, Unit Unit, bool IsActive);

public sealed record DishIngredientView(Guid ProductId, string ProductName, Unit Unit, decimal Quantity);

public sealed record DishView(Guid Id, string Name, bool IsActive, IReadOnlyList<DishIngredientView> Ingredients);

public sealed record WriteOffView(Guid ProductId, string ProductName, Unit Unit, MovementType Type, decimal Quantity);

public sealed record DeficitView(
    Guid ProductId,
    string ProductName,
    Unit Unit,
    int RejectionCount,
    decimal TotalDeficit,
    decimal Available);

public sealed record ExpiryTotalView(string ProductName, Unit Unit, decimal Quantity);

public sealed record ForecastView(string DishName, string ProductName, double HoursUntilStop);

public sealed record WeeklyForecastItemView(
    Guid ProductId,
    string ProductName,
    Unit Unit,
    decimal ConsumedThisWeek,
    decimal ProjectedNeed,
    decimal OnHand,
    decimal Shortfall,
    IReadOnlyList<string> Dishes);

public sealed record WeeklyForecastReport(
    DateTime WeekStart,
    double ElapsedDays,
    int OrdersThisWeek,
    IReadOnlyList<WeeklyForecastItemView> Items);

public sealed record PurchaseDraft(string Payload, string Number, int ItemCount);

public sealed record PurchaseQrLine(Guid ProductId, string Name, string Unit, decimal Quantity, double ShelfLifeHours);

public sealed record DashboardView(
    IReadOnlyList<BatchView> Expiring,
    int StoppedCount,
    IReadOnlyList<ExpiryTotalView> ExpiryWriteOffToday,
    int AcceptedOrdersToday,
    IReadOnlyList<ForecastView> Forecasts,
    WeeklyForecastReport WeeklyForecast);

public sealed record ServiceResult<T>(T? Value, string? Error)
{
    public static ServiceResult<T> Ok(T value) => new(value, null);

    public static ServiceResult<T> Bad(string error) => new(default, error);
}
