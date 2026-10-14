using Microsoft.EntityFrameworkCore;
using Monitoring.Core;
using Monitoring.Core.Inventory;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

public sealed record ShiftHistoryView(Guid Id, Guid DishId, DateTime At, string ActorLogin, string Text);

public sealed record ShiftCurrentView(bool IsOpen, DateTime? OpenedAt, DateTime? ClosedAt);

public sealed record ShiftJournalRow(Guid Id, DateTime OpenedAt, DateTime? ClosedAt, string OpenedBy, string? ClosedBy, bool IsOpen);

public sealed record OpenShiftDish(Guid DishId, int Portions);

/// <summary>Рука шефа: стоп, порции заготовки и приоритет. Килограммы склада не двигает.</summary>
public sealed class ShiftService(AppDbContext db, IClock clock, StockReadModel stock, StockBroadcaster broadcaster)
{
    /// <summary>Журнал смен для менеджера: кто открыл, кто закрыл и сколько длилась.</summary>
    public async Task<IReadOnlyList<ShiftJournalRow>> JournalAsync(CancellationToken cancellationToken)
    {
        var rows = await db.ServiceShifts.AsNoTracking()
            .Include(row => row.OpenedByUser)
            .Include(row => row.ClosedByUser)
            .OrderByDescending(row => row.OpenedAt)
            .Take(30)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new ShiftJournalRow(
            row.Id,
            row.OpenedAt,
            row.ClosedAt,
            Person(row.OpenedByUser),
            row.ClosedByUser is null ? null : Person(row.ClosedByUser),
            row.ClosedAt is null)).ToList();
    }

    /// <summary>Открыта ли смена. ClosedAt последней закрытой нужен, чтобы зал понимал, что день уже кончился.</summary>
    public async Task<ShiftCurrentView> CurrentAsync(CancellationToken cancellationToken)
    {
        var open = await db.ServiceShifts.AsNoTracking()
            .Where(row => row.ClosedAt == null)
            .OrderByDescending(row => row.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (open is not null)
        {
            return new ShiftCurrentView(true, open.OpenedAt, null);
        }

        var last = await db.ServiceShifts.AsNoTracking()
            .OrderByDescending(row => row.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return new ShiftCurrentView(false, last?.OpenedAt, last?.ClosedAt);
    }

    /// <summary>
    /// Открывает день. Вход: порции заготовки на каждое активное блюдо с рецептом, ноль допустим.
    /// Вчерашние стоп и приоритет снимаются. Ноль сразу закрывает блюдо для зала.
    /// </summary>
    public async Task<ServiceResult<ShiftCurrentView>> OpenAsync(
        IReadOnlyList<OpenShiftDish> dishes,
        Guid actorId,
        string actorLogin,
        CancellationToken cancellationToken)
    {
        if (await IsOpenAsync(cancellationToken))
        {
            return ServiceResult<ShiftCurrentView>.Bad("Смена уже открыта.");
        }

        if (dishes.Any(row => row.Portions < 0))
        {
            return ServiceResult<ShiftCurrentView>.Bad("Порции заготовки не бывают отрицательными.");
        }

        var required = await db.Dishes.AsNoTracking()
            .Where(dish => dish.IsActive && dish.Ingredients.Any())
            .Select(dish => dish.Id)
            .ToListAsync(cancellationToken);
        var given = dishes.GroupBy(row => row.DishId).ToDictionary(group => group.Key, group => group.Last().Portions);
        if (required.Any(id => !given.ContainsKey(id)))
        {
            return ServiceResult<ShiftCurrentView>.Bad("Укажите заготовку для каждого блюда.");
        }

        var now = clock.UtcNow;
        db.ServiceShifts.Add(new ServiceShift { Id = Guid.NewGuid(), OpenedAt = now, OpenedBy = actorId });
        foreach (var dishId in required)
        {
            var portions = given[dishId];
            var state = await LoadOrCreateAsync(dishId, cancellationToken);
            state.PrepPortions = portions;
            state.IsPriority = false;
            state.IsStopped = portions == 0;
            state.StopReason = portions == 0 ? nameof(StopReason.OutOfProduct) : null;
            state.StopNote = portions == 0 ? "Заготовка не задана" : null;
            state.StoppedBy = portions == 0 ? actorId : null;
            state.StoppedAt = portions == 0 ? now : null;
            AddHistory(dishId, actorLogin, portions == 0 ? "Смена открыта, заготовка 0 — стоп" : $"Смена открыта, заготовка {portions}");
        }

        await db.SaveChangesAsync(cancellationToken);
        await broadcaster.PublishAsync(new StockChange([], [], [], null, null, null), cancellationToken);
        return ServiceResult<ShiftCurrentView>.Ok(new ShiftCurrentView(true, now, null));
    }

    /// <summary>Закрывает день. Зал перестаёт брать заказы, заготовки до следующего открытия не живут.</summary>
    public async Task<ServiceResult<ShiftCurrentView>> CloseAsync(Guid actorId, string actorLogin, CancellationToken cancellationToken)
    {
        var open = await db.ServiceShifts.FirstOrDefaultAsync(row => row.ClosedAt == null, cancellationToken);
        if (open is null)
        {
            return ServiceResult<ShiftCurrentView>.Bad("Смена уже закрыта.");
        }

        var now = clock.UtcNow;
        open.ClosedAt = now;
        open.ClosedBy = actorId;
        var dishIds = await db.ShiftDishes.Select(row => row.DishId).ToListAsync(cancellationToken);
        foreach (var dishId in dishIds)
        {
            AddHistory(dishId, actorLogin, "Смена закрыта");
        }

        await db.SaveChangesAsync(cancellationToken);
        await broadcaster.PublishAsync(new StockChange([], [], [], null, null, null), cancellationToken);
        return ServiceResult<ShiftCurrentView>.Ok(new ShiftCurrentView(false, open.OpenedAt, now));
    }

    /// <summary>
    /// Вход: блюдо, причина, заметка, кто нажал. Порции становятся 0, приоритет снимается, пишется история.
    /// Выход: плитка или текст ошибки. После сохранения шлёт обновление смены.
    /// </summary>
    public async Task<ServiceResult<ShiftTile>> StopAsync(
        Guid dishId,
        string reason,
        string? note,
        Guid actorId,
        string actorLogin,
        CancellationToken cancellationToken)
    {
        if (!await IsOpenAsync(cancellationToken))
        {
            return ServiceResult<ShiftTile>.Bad("Сначала откройте смену.");
        }

        if (!Enum.TryParse<StopReason>(reason, true, out var parsed))
        {
            return ServiceResult<ShiftTile>.Bad("Причина стопа: OutOfProduct, Waste, Quality, WontMakeIt или Equipment.");
        }

        var dish = await db.Dishes.FirstOrDefaultAsync(item => item.Id == dishId && item.IsActive, cancellationToken);
        if (dish is null)
        {
            return ServiceResult<ShiftTile>.Bad("Блюдо не найдено.");
        }

        var state = await LoadOrCreateAsync(dishId, cancellationToken);
        state.PrepPortions = 0;
        state.IsStopped = true;
        state.IsPriority = false;
        state.StopReason = parsed.ToString();
        state.StopNote = string.IsNullOrWhiteSpace(note) ? null : Trim(note);
        state.StoppedBy = actorId;
        state.StoppedAt = clock.UtcNow;
        AddHistory(dishId, actorLogin, $"Стоп: {Label(parsed)}{(state.StopNote is null ? "" : $" — {state.StopNote}")}");
        await db.SaveChangesAsync(cancellationToken);
        await broadcaster.PublishAsync(new StockChange([], [], [], null, null, null), cancellationToken);
        return ServiceResult<ShiftTile>.Ok((await TileAsync(dishId, cancellationToken))!);
    }

    /// <summary>Снимает стоп. Если порций нет или ноль — ошибка «сначала добавьте порции».</summary>
    public async Task<ServiceResult<ShiftTile>> ResumeAsync(Guid dishId, string actorLogin, CancellationToken cancellationToken)
    {
        if (!await IsOpenAsync(cancellationToken))
        {
            return ServiceResult<ShiftTile>.Bad("Сначала откройте смену.");
        }

        var state = await db.ShiftDishes.FirstOrDefaultAsync(row => row.DishId == dishId, cancellationToken);
        if (state is null || !state.IsStopped)
        {
            return ServiceResult<ShiftTile>.Bad("Блюдо не в стопе.");
        }

        if (state.PrepPortions is null or <= 0)
        {
            return ServiceResult<ShiftTile>.Bad("Сначала добавьте порции, потом снимайте стоп.");
        }

        state.IsStopped = false;
        state.StopReason = null;
        state.StopNote = null;
        state.StoppedBy = null;
        state.StoppedAt = null;
        AddHistory(dishId, actorLogin, "Сняли стоп");
        await db.SaveChangesAsync(cancellationToken);
        await broadcaster.PublishAsync(new StockChange([], [], [], null, null, null), cancellationToken);
        return ServiceResult<ShiftTile>.Ok((await TileAsync(dishId, cancellationToken))!);
    }

    /// <summary>
    /// Вход: дельта порций. Ниже нуля нельзя. Стало 0 — стоп OutOfProduct.
    /// Было в стопе и стало больше нуля — стоп снимается. Это не приход на склад.
    /// </summary>
    public async Task<ServiceResult<ShiftTile>> PortionsAsync(Guid dishId, int delta, string actorLogin, CancellationToken cancellationToken)
    {
        if (!await IsOpenAsync(cancellationToken))
        {
            return ServiceResult<ShiftTile>.Bad("Сначала откройте смену.");
        }

        if (delta == 0)
        {
            return ServiceResult<ShiftTile>.Bad("Дельта порций не может быть нулём.");
        }

        var dish = await db.Dishes.FirstOrDefaultAsync(item => item.Id == dishId && item.IsActive, cancellationToken);
        if (dish is null)
        {
            return ServiceResult<ShiftTile>.Bad("Блюдо не найдено.");
        }

        var state = await LoadOrCreateAsync(dishId, cancellationToken);
        var current = state.PrepPortions ?? 0;
        var next = current + delta;
        if (next < 0)
        {
            return ServiceResult<ShiftTile>.Bad("Порций не может быть меньше нуля.");
        }

        state.PrepPortions = next;
        if (next == 0)
        {
            state.IsStopped = true;
            state.IsPriority = false;
            state.StopReason = nameof(StopReason.OutOfProduct);
            state.StopNote = "Нет порций на заготовке";
            state.StoppedAt = clock.UtcNow;
            AddHistory(dishId, actorLogin, delta > 0 ? $"+{delta} порц. → стоп" : $"{delta} порц. → стоп");
        }
        else
        {
            if (state.IsStopped)
            {
                state.IsStopped = false;
                state.StopReason = null;
                state.StopNote = null;
                state.StoppedAt = null;
            }

            AddHistory(dishId, actorLogin, delta > 0 ? $"+{delta} порц., осталось {next}" : $"{delta} порц., осталось {next}");
        }

        await db.SaveChangesAsync(cancellationToken);
        await broadcaster.PublishAsync(new StockChange([], [], [], null, null, null), cancellationToken);
        return ServiceResult<ShiftTile>.Ok((await TileAsync(dishId, cancellationToken))!);
    }

    /// <summary>Включает или выключает «предлагать залу». Стоп и ноль порций в приоритет не ставятся.</summary>
    public async Task<ServiceResult<ShiftTile>> PriorityAsync(Guid dishId, bool on, string actorLogin, CancellationToken cancellationToken)
    {
        if (!await IsOpenAsync(cancellationToken))
        {
            return ServiceResult<ShiftTile>.Bad("Сначала откройте смену.");
        }

        var state = await LoadOrCreateAsync(dishId, cancellationToken);
        if (state.IsStopped || state.PrepPortions == 0)
        {
            return ServiceResult<ShiftTile>.Bad("Стоп нельзя поставить в приоритет.");
        }

        state.IsPriority = on;
        AddHistory(dishId, actorLogin, on ? "В приоритет — предлагать залу" : "Убрали из приоритета");
        await db.SaveChangesAsync(cancellationToken);
        await broadcaster.PublishAsync(new StockChange([], [], [], null, null, null), cancellationToken);
        return ServiceResult<ShiftTile>.Ok((await TileAsync(dishId, cancellationToken))!);
    }

    /// <summary>Последние события смены. dishId сужает до одного блюда. Выход: кто, когда, текст.</summary>
    public async Task<IReadOnlyList<ShiftHistoryView>> HistoryAsync(Guid? dishId, CancellationToken cancellationToken)
    {
        var query = db.ShiftHistory.AsNoTracking().OrderByDescending(row => row.At).AsQueryable();
        if (dishId is Guid id)
        {
            query = query.Where(row => row.DishId == id);
        }

        return await query.Take(40).Select(row => new ShiftHistoryView(row.Id, row.DishId, row.At, row.ActorLogin, row.Text)).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Минусует заготовку внутри чужой транзакции заказа и сам не сохраняет.
    /// Блюдо без лимита пропускает. Дошли до нуля — стоп и запись в историю.
    /// </summary>
    public async Task ApplyOrderPortionsAsync(IReadOnlyList<(Guid DishId, int Portions)> lines, CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            var state = await db.ShiftDishes.FirstOrDefaultAsync(row => row.DishId == line.DishId, cancellationToken);
            if (state?.PrepPortions is not int prep)
            {
                continue;
            }

            var next = Math.Max(0, prep - line.Portions);
            state.PrepPortions = next;
            if (next == 0)
            {
                state.IsStopped = true;
                state.IsPriority = false;
                state.StopReason = nameof(StopReason.OutOfProduct);
                state.StopNote = "Порции заготовки закончились";
                state.StoppedAt = clock.UtcNow;
                AddHistory(line.DishId, "зал", $"Заказ −{line.Portions} → стоп");
            }
            else
            {
                AddHistory(line.DishId, "зал", $"Заказ −{line.Portions}, осталось {next}");
            }
        }
    }

    private Task<bool> IsOpenAsync(CancellationToken cancellationToken) =>
        db.ServiceShifts.AnyAsync(row => row.ClosedAt == null, cancellationToken);

    private async Task<ShiftDish> LoadOrCreateAsync(Guid dishId, CancellationToken cancellationToken)
    {
        var state = await db.ShiftDishes.FirstOrDefaultAsync(row => row.DishId == dishId, cancellationToken);
        if (state is not null)
        {
            return state;
        }

        state = new ShiftDish { DishId = dishId };
        db.ShiftDishes.Add(state);
        return state;
    }

    private async Task<ShiftTile?> TileAsync(Guid dishId, CancellationToken cancellationToken) =>
        (await stock.StopListAsync(cancellationToken)).FirstOrDefault(tile => tile.DishId == dishId);

    private void AddHistory(Guid dishId, string actor, string text) =>
        db.ShiftHistory.Add(new ShiftHistory
        {
            Id = Guid.NewGuid(),
            DishId = dishId,
            At = clock.UtcNow,
            ActorLogin = actor,
            Text = Trim(text),
        });

    private static string Person(UserAccount user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? user.Login : user.DisplayName;

    private static string Trim(string value) => value.Trim().Length <= 300 ? value.Trim() : value.Trim()[..300];

    private static string Label(StopReason reason) => reason switch
    {
        StopReason.OutOfProduct => "закончился продукт",
        StopReason.Waste => "брак",
        StopReason.Quality => "качество",
        StopReason.WontMakeIt => "не успеем",
        StopReason.Equipment => "техника",
        _ => reason.ToString(),
    };
}
