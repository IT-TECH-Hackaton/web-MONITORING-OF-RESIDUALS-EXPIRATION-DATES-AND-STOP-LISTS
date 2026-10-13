using Monitoring.Core;

namespace Monitoring.Infrastructure.Persistence;

/// <summary>Учётная запись. Пароль хранится только как хеш.</summary>
public sealed class UserAccount
{
    public Guid Id { get; set; }
    public string Login { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
}

/// <summary>Сырьё склада. Остаток живёт в партиях, не в этом ряду.</summary>
public sealed class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Unit Unit { get; set; }
    /// <summary>Закупочная цена за единицу склада, рубли. Партия копирует её в момент прихода.</summary>
    public decimal PurchasePrice { get; set; }
    public bool IsActive { get; set; } = true;
    public List<DishIngredient> Ingredients { get; set; } = [];
    public List<Batch> Batches { get; set; } = [];
}

/// <summary>Блюдо меню. Сколько можно продать на смене — в ShiftDish, не здесь.</summary>
public sealed class Dish
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Цена порции в меню, рубли.</summary>
    public decimal SalePrice { get; set; }
    public bool IsActive { get; set; } = true;
    public List<DishIngredient> Ingredients { get; set; } = [];
}

/// <summary>Техкарта: сколько продукта списывается с одной порции блюда.</summary>
public sealed class DishIngredient
{
    public Guid DishId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public Dish Dish { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

/// <summary>Поставка. Повтор того же Id не создаёт вторую приёмку.</summary>
public sealed class Delivery
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public string? Supplier { get; set; }
    public Guid? ReceivedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public UserAccount? ReceivedByUser { get; set; }
    public List<Batch> Batches { get; set; } = [];
}

/// <summary>Партия: килограммы или миллилитры, срок, сертификат. Это склад, не порции заготовки.</summary>
public sealed class Batch
{
    public Guid Id { get; set; }
    public Guid DeliveryId { get; set; }
    public Guid ProductId { get; set; }
    public decimal QuantityInitial { get; set; }
    public decimal QuantityRemaining { get; set; }
    /// <summary>Рубли за единицу на момент прихода. Себестоимость списания берётся отсюда.</summary>
    public decimal UnitCost { get; set; }
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

/// <summary>Заказ зала. Отказ тоже сохраняется, чтобы видеть дефицит. TableNumber обязателен у официанта.</summary>
public sealed class CustomerOrder
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public OrderStatus Status { get; set; }
    public string? RejectionReason { get; set; }
    public string? TableNumber { get; set; }
    public UserAccount CreatedByUser { get; set; } = null!;
    public List<OrderItem> Items { get; set; } = [];
    public List<OrderShortage> Shortages { get; set; } = [];
}

/// <summary>Строка заказа. KitchenStatus — старая очередь, новый экран смены её не показывает.</summary>
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

/// <summary>Чего не хватило, если заказ отклонили по складу. При стопе шефа строк нет.</summary>
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

/// <summary>Движение партии. Reason — статья акта (Expiry, Waste и т.д.), у заказа может быть пусто.</summary>
public sealed class StockMovement
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid? OrderId { get; set; }
    public MovementType Type { get; set; }
    public decimal Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Reason { get; set; }
    public Batch Batch { get; set; } = null!;
}

/// <summary>
/// Состояние блюда на смене. PrepPortions null — шеф лимит не ставил, зал не режется складом.
/// Ноль порций или IsStopped закрывают блюдо. Это не остаток в килограммах.
/// </summary>
public sealed class ShiftDish
{
    public Guid DishId { get; set; }
    public int? PrepPortions { get; set; }
    public bool IsStopped { get; set; }
    public string? StopReason { get; set; }
    public string? StopNote { get; set; }
    public Guid? StoppedBy { get; set; }
    public DateTime? StoppedAt { get; set; }
    public bool IsPriority { get; set; }
    public Dish Dish { get; set; } = null!;
}

/// <summary>Кто и когда поставил или снял стоп и двигал порции. Складские движения сюда не пишутся.</summary>
public sealed class ShiftHistory
{
    public Guid Id { get; set; }
    public Guid DishId { get; set; }
    public DateTime At { get; set; }
    public string ActorLogin { get; set; } = "";
    public string Text { get; set; } = "";
    public Dish Dish { get; set; } = null!;
}
