using Microsoft.EntityFrameworkCore;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

/// <summary>Справочник продуктов и техкарт. Склад и смену не меняет.</summary>
public sealed class CatalogService(AppDbContext db)
{
    /// <summary>Активные продукты: id, имя, единица.</summary>
    public async Task<IReadOnlyList<ProductView>> ProductsAsync(CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking()
            .Where(product => product.IsActive)
            .OrderBy(product => product.Name)
            .Select(product => new ProductView(product.Id, product.Name, product.Unit, product.IsActive, product.PurchasePrice))
            .ToListAsync(cancellationToken);

    /// <summary>Вход: имя и единица. Пустое имя — ошибка. Выход: созданный продукт.</summary>
    public async Task<ServiceResult<ProductView>> CreateProductAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return ServiceResult<ProductView>.Bad("Укажите название продукта.");
        }

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = command.Name.Trim(),
            Unit = command.Unit,
            IsActive = true,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<ProductView>.Ok(new ProductView(product.Id, product.Name, product.Unit, product.IsActive, product.PurchasePrice));
    }

    /// <summary>Блюда со строками техкарты.</summary>
    public async Task<IReadOnlyList<DishView>> DishesAsync(CancellationToken cancellationToken)
    {
        var dishes = await db.Dishes.AsNoTracking()
            .Include(dish => dish.Ingredients)
            .ThenInclude(line => line.Product)
            .Where(dish => dish.IsActive)
            .OrderBy(dish => dish.Name)
            .ToListAsync(cancellationToken);
        return dishes.Select(Map).ToList();
    }

    /// <summary>Вход: имя и ингредиенты. Выход: блюдо. Продукт в строке должен существовать.</summary>
    public async Task<ServiceResult<DishView>> CreateDishAsync(UpsertDishCommand command, CancellationToken cancellationToken)
    {
        var error = await ValidateDishAsync(command, cancellationToken);
        if (error is not null)
        {
            return ServiceResult<DishView>.Bad(error);
        }

        var dish = new Dish
        {
            Id = Guid.NewGuid(),
            Name = command.Name.Trim(),
            IsActive = true,
            Ingredients = command.Ingredients.Select(line => new DishIngredient
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
            }).ToList(),
        };
        db.Dishes.Add(dish);
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<DishView>.Ok((await DishesAsync(cancellationToken)).Single(item => item.Id == dish.Id));
    }

    /// <summary>Заменяет имя и техкарту блюда. Старые строки рецепта стирает и пишет новые.</summary>
    public async Task<ServiceResult<DishView>> UpdateDishAsync(Guid id, UpsertDishCommand command, CancellationToken cancellationToken)
    {
        var dish = await db.Dishes.Include(item => item.Ingredients).FirstOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken);
        if (dish is null)
        {
            return ServiceResult<DishView>.Bad("Блюдо не найдено.");
        }

        var error = await ValidateDishAsync(command, cancellationToken);
        if (error is not null)
        {
            return ServiceResult<DishView>.Bad(error);
        }

        dish.Name = command.Name.Trim();
        var incoming = command.Ingredients.ToDictionary(line => line.ProductId);
        foreach (var line in dish.Ingredients.ToList())
        {
            if (!incoming.TryGetValue(line.ProductId, out var next))
            {
                db.DishIngredients.Remove(line);
                continue;
            }

            line.Quantity = next.Quantity;
        }

        foreach (var line in command.Ingredients)
        {
            if (dish.Ingredients.All(existing => existing.ProductId != line.ProductId))
            {
                dish.Ingredients.Add(new DishIngredient
                {
                    DishId = dish.Id,
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<DishView>.Ok((await DishesAsync(cancellationToken)).Single(item => item.Id == dish.Id));
    }

    private async Task<string?> ValidateDishAsync(UpsertDishCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return "Укажите название блюда.";
        }

        if (command.Ingredients.Count == 0)
        {
            return "Добавьте хотя бы один ингредиент.";
        }

        if (command.Ingredients.Any(line => line.Quantity <= 0))
        {
            return "Количество ингредиента должно быть больше нуля.";
        }

        if (command.Ingredients.Select(line => line.ProductId).Distinct().Count() != command.Ingredients.Count)
        {
            return "Продукт в рецепте указан дважды.";
        }

        var ids = command.Ingredients.Select(line => line.ProductId).ToList();
        var found = await db.Products.CountAsync(product => ids.Contains(product.Id) && product.IsActive, cancellationToken);
        return found == ids.Count ? null : "В рецепте указан неизвестный продукт.";
    }

    private static DishView Map(Dish dish) => new(
        dish.Id,
        dish.Name,
        dish.IsActive,
        dish.Ingredients
            .OrderBy(line => line.Product.Name)
            .Select(line => new DishIngredientView(line.ProductId, line.Product.Name, line.Product.Unit, line.Quantity))
            .ToList(),
        dish.SalePrice);
}
