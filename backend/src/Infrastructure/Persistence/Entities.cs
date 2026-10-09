using Monitoring.Core;

namespace Monitoring.Infrastructure.Persistence;

public sealed class UserAccount
{
    public Guid Id { get; set; }
    public string Login { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
}

public sealed class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Unit Unit { get; set; }
    public bool IsActive { get; set; } = true;
    public List<DishIngredient> Ingredients { get; set; } = [];
    public List<Batch> Batches { get; set; } = [];
}

public sealed class Dish
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public List<DishIngredient> Ingredients { get; set; } = [];
}

public sealed class DishIngredient
{
    public Guid DishId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public Dish Dish { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public sealed class Delivery
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public string? Supplier { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Batch> Batches { get; set; } = [];
}

public sealed class Batch
{
    public Guid Id { get; set; }
    public Guid DeliveryId { get; set; }
    public Guid ProductId { get; set; }
    public decimal QuantityInitial { get; set; }
    public decimal QuantityRemaining { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public BatchStatus Status { get; set; }
    public string? CertificateNumber { get; set; }
    public string? CertificateIssuer { get; set; }
    public DateTime? CertificateIssuedAt { get; set; }
    public DateTime? CertificateExpiresAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public string? KitchenNote { get; set; }
    public Delivery Delivery { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public List<StockMovement> Movements { get; set; } = [];
}

public sealed class CustomerOrder
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public OrderStatus Status { get; set; }
    public string? RejectionReason { get; set; }
    public UserAccount CreatedByUser { get; set; } = null!;
    public List<OrderItem> Items { get; set; } = [];
    public List<OrderShortage> Shortages { get; set; } = [];
}

public sealed class OrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid DishId { get; set; }
    public int Portions { get; set; }
    public KitchenStatus KitchenStatus { get; set; } = KitchenStatus.Queued;
    public string? KitchenNote { get; set; }
    public CustomerOrder Order { get; set; } = null!;
    public Dish Dish { get; set; } = null!;
}

public sealed class OrderShortage
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Required { get; set; }
    public decimal Available { get; set; }
    public CustomerOrder Order { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public sealed class StockMovement
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid? OrderId { get; set; }
    public MovementType Type { get; set; }
    public decimal Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public Batch Batch { get; set; } = null!;
}
