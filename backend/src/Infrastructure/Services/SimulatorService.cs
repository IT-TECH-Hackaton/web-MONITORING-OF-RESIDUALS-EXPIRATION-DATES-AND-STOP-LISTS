using Microsoft.EntityFrameworkCore;
using Monitoring.Core;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

/// <summary>Учебные поставка и заказ. Ходят в те же сервисы, что и боевые ручки.</summary>
public sealed class SimulatorService(AppDbContext db, IClock clock, DeliveryService deliveries, OrderService orders)
{
    /// <summary>Создаёт одну случайную поставку на существующий продукт. Выход — как у приёмки.</summary>
    public async Task<ServiceResult<DeliveryReceipt>> DeliveryAsync(CancellationToken cancellationToken)
    {
        var products = await db.Products.Where(product => product.IsActive).ToListAsync(cancellationToken);
        if (products.Count == 0)
        {
            return ServiceResult<DeliveryReceipt>.Bad("В каталоге нет продуктов для имитации поставки.");
        }

        var count = Random.Shared.Next(1, Math.Min(3, products.Count) + 1);
        var picked = products.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
        var items = picked.Select(product => new DeliveryLineCommand(
            product.Id,
            product.Name,
            product.Unit,
            product.Unit == Unit.Pcs ? Random.Shared.Next(5, 30) : Random.Shared.Next(4, 20) * 50,
            clock.UtcNow.AddHours(Random.Shared.Next(4, 200)),
            null)).ToList();

        return await deliveries.ReceiveAsync(
            new ReceiveDeliveryCommand(Guid.NewGuid(), $"П-{Random.Shared.Next(1000, 9999)}", clock.UtcNow, items),
            cancellationToken);
    }

    /// <summary>Вход: кто нажал. Собирает заказ из случайного блюда и проводит его как обычный, без обязательного стола.</summary>
    public async Task<OrderPlacement> OrderAsync(Guid userId, CancellationToken cancellationToken)
    {
        var dishes = await db.Dishes.Where(dish => dish.IsActive).Select(dish => dish.Id).ToListAsync(cancellationToken);
        if (dishes.Count == 0)
        {
            return OrderPlacement.Invalid("В меню нет блюд для имитации заказа.");
        }

        var dishId = dishes[Random.Shared.Next(dishes.Count)];
        return await orders.PlaceAsync(
            new PlaceOrderCommand(Guid.NewGuid(), userId, null, [new OrderLineCommand(dishId, 1)]),
            cancellationToken);
    }
}
