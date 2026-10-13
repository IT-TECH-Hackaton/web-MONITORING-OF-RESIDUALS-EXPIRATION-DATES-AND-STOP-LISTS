namespace Monitoring.Core;

/// <summary>Единица склада: граммы, миллилитры или штуки. На порции блюда не переводится.</summary>
public enum Unit
{
    G,
    Ml,
    Pcs,
}

/// <summary>Партия: живая, выбранная до нуля или списанная по сроку.</summary>
public enum BatchStatus
{
    Active,
    Depleted,
    Expired,
}

/// <summary>Заказ принят и склад списан, либо отказ сохранён без движения склада.</summary>
public enum OrderStatus
{
    Accepted,
    Rejected,
}

/// <summary>Откуда движение: приход, заказ, автосписание срока, акт кухни.</summary>
public enum MovementType
{
    Receipt,
    OrderWriteOff,
    ExpiryWriteOff,
    KitchenWriteOff,
}

/// <summary>Менеджер видит всё. Официант заказывает. Кухня ведёт смену и акты, заказы не создаёт.</summary>
public enum UserRole
{
    Manager,
    Waiter,
    Kitchen,
}

/// <summary>Старая очередь тикета. Новый экран смены эти статусы не показывает.</summary>
public enum KitchenStatus
{
    Queued,
    Cooking,
    Ready,
}

/// <summary>Статья акта и автосписания: срок, брак, порча, проработка, питание персонала.</summary>
public enum WriteOffReason
{
    Expiry,
    Waste,
    Spoilage,
    Training,
    StaffMeal,
}

/// <summary>Почему шеф закрыл блюдо. Нехватка по техкарте сюда не пишется — это кандидат.</summary>
public enum StopReason
{
    OutOfProduct,
    Waste,
    Quality,
    WontMakeIt,
    Equipment,
}

/// <summary>Свежесть партии для плитки склада: норма, скоро срок, просрочено, пусто.</summary>
public enum Freshness
{
    Ok,
    Expiring,
    Expired,
    Empty,
}
