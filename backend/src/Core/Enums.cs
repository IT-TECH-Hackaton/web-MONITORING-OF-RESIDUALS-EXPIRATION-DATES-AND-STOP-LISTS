namespace Monitoring.Core;

/// <summary>Единица склада: граммы, миллилитры или штуки. На порции блюда не переводится.</summary>
public enum Unit
{
    G,
    Ml,
    Pcs,
}

/// <summary>Партия: приехала и ещё не на складе, живая, под списание человеком, выбранная до нуля или списанная по сроку.</summary>
public enum BatchStatus
{
    Arrived,
    Active,
    PendingWriteOff,
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

/// <summary>Менеджер видит всё. Официант заказывает. Кухня ведёт смену и акты. Поставка только присылает предложение.</summary>
public enum UserRole
{
    Manager,
    Waiter,
    Kitchen,
    Supply,
}

/// <summary>Предложение ждёт, кухня принимает, менеджер подтверждает итог. Accepted — уже на складе.</summary>
public enum DeliveryStage
{
    Parked,
    Receiving,
    KitchenDone,
    Accepted,
}

/// <summary>Старая очередь тикета. Новый экран смены эти статусы не показывает.</summary>
public enum KitchenStatus
{
    Queued,
    Cooking,
    Ready,
    Spoiled,
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

/// <summary>Почему блюдо в стопе: рука шефа, ноль заготовки или склад не тянет на порцию.</summary>
public enum StopReason
{
    OutOfProduct,
    Waste,
    Quality,
    WontMakeIt,
    Equipment,
}

/// <summary>Кто закрыл блюдо для зала. Stock считается сам по техкарте, Chef и Prep пишет смена.</summary>
public enum StopSource
{
    Chef,
    Prep,
    Stock,
}

/// <summary>Свежесть партии для плитки склада: норма, скоро срок, просрочено, пусто.</summary>
public enum Freshness
{
    Ok,
    Expiring,
    Expired,
    Empty,
}

/// <summary>Кто положил строку в заказ поставщику. Склад от этого не растёт.</summary>
public enum PurchaseSource
{
    Chef,
    Manager,
    Forecast,
}
