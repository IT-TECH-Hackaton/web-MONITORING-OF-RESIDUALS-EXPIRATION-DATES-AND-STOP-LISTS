namespace Monitoring.Core.Inventory;

public sealed record RecipeLine(Guid ProductId, string ProductName, decimal QuantityPerPortion);

public sealed record DishRecipe(Guid DishId, string DishName, IReadOnlyList<RecipeLine> Lines);

public sealed record ProductAvailability(decimal Quantity, DateTime? NearestExpiry);

/// <summary>
/// Кандидат по техкарте. IsStopped здесь значит «склада не хватает на порцию», зал от этого не серый.
/// </summary>
public sealed record StopListEntry(
    Guid DishId,
    string DishName,
    int PortionsPossible,
    bool IsStopped,
    string? Reason);

/// <summary>
/// Плитка смены, которую видят зал и шеф.
/// IsStopped — шеф подтвердил 86 или порций заготовки ноль. IsCandidate — нехватка по рецепту, зал ещё открыт.
/// IsLow — лимит задан и порций от 1 до 5. HasLimit false — шеф лимит не ставил, PortionsPossible тогда большое число.
/// </summary>
public sealed record ShiftTile(
    Guid DishId,
    string DishName,
    int PortionsPossible,
    bool IsStopped,
    bool IsCandidate,
    bool IsLow,
    bool IsPriority,
    string? Reason,
    string? CandidateReason,
    string? StopReason,
    bool HasLimit);

/// <summary>Считает, сколько порций выходит из доступных партий. Просроченное и нулевое в остаток не входит.</summary>
public static class StopListCalculator
{
    /// <summary>
    /// Вход: техкарты и остаток по продуктам. Выход: по блюду число порций и текст нехватки.
    /// Блюдо без строк рецепта пропускает. Это не стоп зала.
    /// </summary>
    public static IReadOnlyList<StopListEntry> Calculate(
        IReadOnlyList<DishRecipe> dishes,
        IReadOnlyDictionary<Guid, ProductAvailability> stock)
    {
        var result = new List<StopListEntry>();
        foreach (var dish in dishes)
        {
            if (dish.Lines.Count == 0)
            {
                continue;
            }

            var portions = int.MaxValue;
            var reasons = new List<string>();
            foreach (var line in dish.Lines)
            {
                stock.TryGetValue(line.ProductId, out var availability);
                var quantity = availability?.Quantity ?? 0m;
                var canMake = line.QuantityPerPortion <= 0
                    ? 0
                    : (int)Math.Floor(quantity / line.QuantityPerPortion);
                portions = Math.Min(portions, canMake);

                if (quantity < line.QuantityPerPortion)
                {
                    var deficit = line.QuantityPerPortion - quantity;
                    var expiry = availability?.NearestExpiry;
                    var expiryText = expiry is null
                        ? ""
                        : $", ближайшая партия до {expiry.Value:dd.MM HH:mm} UTC";
                    reasons.Add($"{line.ProductName}: не хватает {FormatQuantity(deficit)}{expiryText}");
                }
            }

            var stopped = reasons.Count > 0;
            result.Add(new StopListEntry(
                dish.DishId,
                dish.DishName,
                stopped ? 0 : portions,
                stopped,
                stopped ? string.Join("; ", reasons) : null));
        }

        return result;
    }

    public static string FormatQuantity(decimal value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
