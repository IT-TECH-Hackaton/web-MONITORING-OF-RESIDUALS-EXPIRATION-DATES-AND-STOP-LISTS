namespace Monitoring.Core;

public enum Unit
{
    G,
    Ml,
    Pcs,
}

public enum BatchStatus
{
    Active,
    Depleted,
    Expired,
}

public enum OrderStatus
{
    Accepted,
    Rejected,
}

public enum MovementType
{
    Receipt,
    OrderWriteOff,
    ExpiryWriteOff,
    KitchenWriteOff,
}

public enum UserRole
{
    Manager,
    Waiter,
    Kitchen,
}

public enum KitchenStatus
{
    Queued,
    Cooking,
    Ready,
}

public enum Freshness
{
    Ok,
    Expiring,
    Expired,
    Empty,
}
