using System.Security.Claims;
using Monitoring.Api.Auth;
using Monitoring.Api.Swagger;
using Monitoring.Core;
using Monitoring.Core.Inventory;
using Monitoring.Infrastructure.Services;

namespace Monitoring.Api;

/// <summary>
/// HTTP-ручки. Комментарий у группы говорит, кто имеет доступ, что приходит в теле и что уходит в ответе.
/// </summary>
public static class EndpointExtensions
{
    /// <summary>
    /// Вход без токена. Логин: логин и пароль, выход — access и refresh. Refresh: только refresh-токен, выход — новая пара.
    /// 401, если пара неверная или сессия протухла.
    /// </summary>
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapPost("/login", async (LoginRequest request, AuthService auth, JwtTokenService tokens, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password)
                || request.Login.Length > 64 || request.Password.Length > 128)
            {
                return Results.BadRequest(new ErrorResponse("Укажите логин и пароль."));
            }

            var user = await auth.AuthenticateAsync(request.Login.Trim(), request.Password, cancellationToken);
            if (user is null)
            {
                return Results.Json(new ErrorResponse("Неверный логин или пароль"), statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!user.Approved)
            {
                return Results.Json(new ErrorResponse("Заявку ещё не подтвердил менеджер."), statusCode: StatusCodes.Status403Forbidden);
            }

            var issued = tokens.Issue(user);
            await auth.BindRefreshAsync(user.Id, issued.RefreshToken, cancellationToken);
            return Results.Ok(ToResponse(issued));
        })
        .Docs("Вход", "Логин", "Вход: логин и пароль. Выход: access, refresh, роль, логин и id. 401 — пара неверная.")
        .RequireRateLimiting("auth")
        .Ok<AuthSessionResponse>()
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);
        group.MapPost("/refresh", async (RefreshRequest request, AuthService auth, JwtTokenService tokens, CancellationToken cancellationToken) =>
        {
            var parsed = tokens.ReadRefresh(request.RefreshToken);
            if (parsed is null)
            {
                return Results.Json(new { message = "Сессия истекла" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!await auth.RefreshMatchesAsync(parsed.Id, request.RefreshToken, cancellationToken))
            {
                return Results.Json(new ErrorResponse("Сессия истекла"), statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await auth.FindAsync(parsed.Id, cancellationToken);
            if (user is null)
            {
                return Results.Json(new ErrorResponse("Сессия истекла"), statusCode: StatusCodes.Status401Unauthorized);
            }

            var issued = tokens.Issue(user);
            await auth.BindRefreshAsync(user.Id, issued.RefreshToken, cancellationToken);
            return Results.Ok(ToResponse(issued));
        })
        .Docs("Вход", "Обновить пару токенов", "Вход: только refresh. Access сюда не принимается. Выход — новая пара. 401 — сессия мертва.")
        .RequireRateLimiting("auth")
        .Ok<AuthSessionResponse>()
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);
        group.MapPost("/register", async (RegisterRequest request, StaffService staff, CancellationToken cancellationToken) =>
        {
            var result = await staff.RegisterAsync(request.Login, request.Password, request.DisplayName, request.Role, cancellationToken);
            return result.Error is null ? Results.Ok(new RegisterResponse(result.Value!)) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Вход", "Заявка на доступ", "Логин, пароль, имя и роль Waiter или Kitchen. Вход откроется после подтверждения менеджера.")
        .RequireRateLimiting("auth")
        .Ok<RegisterResponse>()
        .Bad();
        group.MapPost("/logout", async (ClaimsPrincipal principal, AuthService auth, CancellationToken cancellationToken) =>
        {
            await auth.ForgetRefreshAsync(UserId(principal), cancellationToken);
            return Results.NoContent();
        })
        .Docs("Вход", "Выйти", "Гасит refresh. Access доживёт свои минуты, новым refresh уже не обменять.")
        .RequireAuthorization();
    }

    /// <summary>Заявки подтверждает менеджер. Чат читают и пишут все три роли.</summary>
    public static void MapStaffEndpoints(this WebApplication app)
    {
        var staff = app.MapGroup("/api/staff").RequireAuthorization("Manager");
        staff.MapGet("/pending", async (StaffService people, CancellationToken cancellationToken) =>
            Results.Ok(await people.PendingAsync(cancellationToken)))
            .Docs("Сотрудники", "Заявки", "Кто попросился на кухню или в зал и ещё не подтверждён.")
            .Ok<IReadOnlyList<StaffApplicationView>>();
        staff.MapPost("/{id:guid}/approve", async (Guid id, StaffService people, CancellationToken cancellationToken) =>
        {
            var result = await people.ApproveAsync(id, cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Сотрудники", "Подтвердить заявку", "После этого человек может войти со своим паролем.")
        .Ok<StaffApplicationView>()
        .Bad();
        staff.MapPost("/{id:guid}/reject", async (Guid id, StaffService people, CancellationToken cancellationToken) =>
        {
            var result = await people.RejectAsync(id, cancellationToken);
            return result.Error is null ? Results.Ok(new RegisterResponse(result.Value!)) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Сотрудники", "Отклонить заявку", "Учётная запись удаляется, войти нельзя.")
        .Ok<RegisterResponse>()
        .Bad();

        var chat = app.MapGroup("/api/chat").RequireAuthorization("Chat");
        chat.MapGet("/", async (StaffService people, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
            Results.Ok(await people.MessagesAsync(UserId(principal), principal.FindFirstValue(ClaimTypes.Role) ?? "", cancellationToken)))
            .Docs("Чат", "Лента", "Общий чат, система и личные сообщения этого сотрудника. Чужие личные не отдаёт.")
            .Ok<IReadOnlyList<ChatMessageView>>();
        chat.MapGet("/people", async (StaffService people, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            if (principal.FindFirstValue(ClaimTypes.Role) == nameof(UserRole.Supply))
            {
                return Results.Ok(Array.Empty<StaffApplicationView>());
            }

            return Results.Ok(await people.ColleaguesAsync(UserId(principal), cancellationToken));
        })
            .Docs("Чат", "Кому написать", "Подтверждённые сотрудники, кроме себя.")
            .Ok<IReadOnlyList<StaffApplicationView>>();
        chat.MapPost("/", async (ChatPostRequest request, StaffService people, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            var result = await people.PostAsync(UserId(principal), request.Text, request.RecipientId, request.ToSupplier, cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Чат", "Написать", "Текст до 500 символов. Сообщение сразу уходит остальным в SignalR.")
        .Ok<ChatMessageView>()
        .Bad();
    }

    /// <summary>
    /// Продукты и блюда. Чтение — любая роль. Создание продукта и блюда, правка техкарты — только менеджер.
    /// Продукт: имя и единица G, Ml или Pcs. Блюдо: имя и строки техкарты (productId, количество на порцию).
    /// </summary>
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        var products = app.MapGroup("/api/products").RequireAuthorization("Staff");
        products.MapGet("/", async (CatalogService catalog, CancellationToken cancellationToken) =>
            Results.Ok(await catalog.ProductsAsync(cancellationToken)))
            .Docs("Каталог", "Продукты", "Активные продукты: id, имя, единица G, Ml или Pcs.")
            .Ok<IReadOnlyList<ProductView>>();
        products.MapPost("/", async (CreateProductRequest request, CatalogService catalog, CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<Unit>(request.Unit, true, out var unit))
            {
                return Results.BadRequest(new ErrorResponse("Единица измерения: G, Ml или Pcs."));
            }

            var result = await catalog.CreateProductAsync(new CreateProductCommand(request.Name, unit), cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Каталог", "Создать продукт", "Вход: имя и единица. Пустое имя или неизвестная единица — 400.")
        .Ok<ProductView>()
        .Bad()
        .RequireAuthorization("Manager");

        var dishes = app.MapGroup("/api/dishes").RequireAuthorization("Staff");
        dishes.MapGet("/", async (CatalogService catalog, CancellationToken cancellationToken) =>
            Results.Ok(await catalog.DishesAsync(cancellationToken)))
            .Docs("Каталог", "Блюда", "Блюда со строками техкарты: продукт и сколько его на одну порцию.")
            .Ok<IReadOnlyList<DishView>>();
        dishes.MapPost("/", async (DishRequest request, CatalogService catalog, CancellationToken cancellationToken) =>
            await SaveDish(catalog, null, request, cancellationToken))
            .Docs("Каталог", "Создать блюдо", "Вход: имя и ингредиенты. Продукт в строке должен существовать.")
            .Ok<DishView>()
            .Bad()
            .RequireAuthorization("Manager");
        dishes.MapPut("/{id:guid}", async (Guid id, DishRequest request, CatalogService catalog, CancellationToken cancellationToken) =>
            await SaveDish(catalog, id, request, cancellationToken))
            .Docs("Каталог", "Заменить техкарту", "Вход: id блюда, имя и новые строки. Старый рецепт стирается.")
            .Ok<DishView>()
            .Bad()
            .RequireAuthorization("Manager");
    }

    /// <summary>
    /// Поставки, только менеджер. Список и карточка — что уже принято.
    /// POST /scan принимает строку QR: сертификат проверяется, без него склад не меняется.
    /// POST / принимает id, номер и строки (продукт, количество, срок). Повтор того же id не создаёт вторую поставку.
    /// </summary>
    public static void MapDeliveryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/deliveries").RequireAuthorization("Manager");
        group.MapGet("/", async (DeliveryService deliveries, CancellationToken cancellationToken) =>
            Results.Ok(await deliveries.ListAsync(cancellationToken)))
            .Docs("Поставки", "Список поставок", "Принятые поставки с партиями, новые сверху.")
            .Ok<IReadOnlyList<DeliveryView>>();
        group.MapGet("/{id:guid}", async (Guid id, DeliveryService deliveries, CancellationToken cancellationToken) =>
        {
            var delivery = await deliveries.GetAsync(id, cancellationToken);
            return delivery is null ? Results.NotFound(new ErrorResponse("Поставка не найдена")) : Results.Ok(delivery);
        })
        .Docs("Поставки", "Карточка поставки", "Вход: id. 404, если такой приёмки нет.")
        .Ok<DeliveryView>()
        .Missing();
        group.MapGet("/qr-sample", (bool? valid, QrIntakeService intake) =>
            Results.Ok(new QrSampleResponse(intake.Sample(valid ?? true))))
            .Docs("Поставки", "Учебный QR", "valid=false собирает QR с заведомо плохим сертификатом. На склад ничего не пишет.")
            .Ok<QrSampleResponse>();
        group.MapPost("/scan", async (QrScanRequest request, QrIntakeService intake, CancellationToken cancellationToken) =>
        {
            var result = await intake.ScanAsync(request.Payload, cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Поставки", "Принять QR", "Вход: строка QR. Годный сертификат создаёт поставку. Без него 400, склад не меняется.")
        .Ok<QrIntakeResult>()
        .Bad();
        group.MapPost("/", async (DeliveryRequest request, DeliveryService deliveries, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            if (request.Items is null || request.Items.Count == 0)
            {
                return Results.BadRequest(new ErrorResponse("В поставке нет продуктов."));
            }

            var lines = new List<DeliveryLineCommand>();
            foreach (var item in request.Items)
            {
                if (!Enum.TryParse<Unit>(item.Unit, true, out var unit))
                {
                    return Results.BadRequest(new ErrorResponse("Единица измерения: G, Ml или Pcs."));
                }

                lines.Add(new DeliveryLineCommand(item.ProductId, item.Name, unit, item.Quantity, item.ExpiresAt, item.ShelfLifeHours));
            }

            var result = await deliveries.ReceiveAsync(
                new ReceiveDeliveryCommand(request.Id, request.Number, request.CreatedAt, lines, request.Supplier, UserId(principal), true),
                cancellationToken);
            if (result.Error is not null || result.Value is null)
            {
                return Results.BadRequest(new ErrorResponse(result.Error!));
            }

            return result.Value.Created
                ? Results.Created($"/api/deliveries/{result.Value.Delivery.Id}", result.Value.Delivery)
                : Results.Ok(result.Value.Delivery);
        })
        .Docs("Поставки", "Отметить приезд", "Накладная становится «приехала». На склад попадает после приёмки кухни и итога. Повтор того же id не создаёт вторую поставку.")
        .Created<DeliveryView>()
        .Ok<DeliveryView>()
        .Bad();
        group.MapPost("/from-order", async (DeliveryService deliveries, CancellationToken cancellationToken) =>
        {
            var result = await deliveries.SendFromOrderAsync(cancellationToken);
            return result.Error is null
                ? Results.Created($"/api/deliveries/{result.Value!.Id}", result.Value)
                : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Поставки", "Заказ приехал", "Открытые строки становятся одной поставкой «приехала». Склад не растёт. Повтор ждёт, пока первая запись закроет строки.")
        .Created<DeliveryView>()
        .Bad();
        group.MapPost("/{id:guid}/receive", async (Guid id, DeliveryService deliveries, CancellationToken cancellationToken) =>
        {
            var result = await deliveries.StartReceivingAsync(id, cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Поставки", "В приёмку", "Поставка «приехала» уходит кухне. Склад не меняется.")
        .Ok<DeliveryView>()
        .Bad();
        group.MapPost("/{id:guid}/accept", async (Guid id, DeliveryService deliveries, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            var result = await deliveries.AcceptAsync(id, UserId(principal), cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Поставки", "Подтвердить итог", "На склад попадает только взятое кухней количество. Ноль не приходуется. Повтор не удваивает остаток.")
        .Ok<DeliveryView>()
        .Bad();

        var intake = app.MapGroup("/api/deliveries").RequireAuthorization("Kitchen");
        intake.MapGet("/intake", async (DeliveryService deliveries, CancellationToken cancellationToken) =>
            Results.Ok(await deliveries.IntakeAsync(cancellationToken)))
            .Docs("Поставки", "Приёмка кухни", "Поставки, которые менеджер отдал в приёмку, и те, где кухня уже отметила количества.")
            .Ok<IReadOnlyList<DeliveryView>>();
        intake.MapPost("/{id:guid}/intake", async (Guid id, IntakeRequest request, DeliveryService deliveries, CancellationToken cancellationToken) =>
        {
            var lines = (request.Lines ?? []).Select(line => new IntakeLineCommand(line.BatchId, line.Quantity, line.ExpiresAt, line.Note)).ToList();
            var result = await deliveries.KitchenDecideAsync(id, lines, cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Поставки", "Решение кухни", "По каждой строке число от 0 до заявленного. Склад не меняется.")
        .Ok<DeliveryView>()
        .Bad();
    }

    /// <summary>Роль «Поставка»: видит открытый заказ и отправляет его как предложение. Склада не касается.</summary>
    public static void MapSupplyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/supply").RequireAuthorization("Supply");
        group.MapGet("/order", async (PurchaseService purchases, CancellationToken cancellationToken) =>
            Results.Ok(await purchases.ListAsync(true, cancellationToken)))
            .Docs("Поставка", "Открытый заказ", "Строки, которые ещё не уехали. Склад от этого списка не растёт.")
            .Ok<IReadOnlyList<PurchaseLineView>>();
        group.MapPost("/send", async (DeliveryService deliveries, CancellationToken cancellationToken) =>
        {
            var result = await deliveries.SendFromOrderAsync(cancellationToken);
            return result.Error is null
                ? Results.Created($"/api/deliveries/{result.Value!.Id}", result.Value)
                : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Поставка", "Сформировать и отправить", "Все открытые строки становятся одной поставкой «приехала». В остаток не входят.")
        .Created<DeliveryView>()
        .Bad();
        group.MapGet("/catalog", async (DeliveryService deliveries, CancellationToken cancellationToken) =>
            Results.Ok(await deliveries.CatalogAsync(cancellationToken)))
            .Docs("Поставка", "Каталог для довоза", "Имена продуктов. Остатков склада здесь нет.")
            .Ok<IReadOnlyList<SupplyProductView>>();
        group.MapPost("/extra", async (SupplyExtraRequest request, DeliveryService deliveries, CancellationToken cancellationToken) =>
        {
            var lines = (request.Items ?? []).Select(item => new SupplyExtraLine(item.ProductId, item.Quantity, item.ExpiresAt)).ToList();
            var result = await deliveries.SendExtraAsync(lines, cancellationToken);
            return result.Error is null
                ? Results.Created($"/api/deliveries/{result.Value!.Id}", result.Value)
                : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Поставка", "Довезли сами", "Позиции сверх заказа. Партии «приехали», открытый заказ и склад не меняются.")
        .Created<DeliveryView>()
        .Bad();
    }

    /// <summary>Машина снаружи. Без токена сотрудника, только заголовок X-Inbound-Key. На склад само не кладёт.</summary>
    public static void MapInboundEndpoints(this WebApplication app)
    {
        app.MapPost("/api/inbound/deliveries", async (HttpRequest http, DeliveryRequest request, DeliveryService deliveries, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            if (!InboundKeyMatches(configuration["Inbound:Key"], http.Headers["X-Inbound-Key"].ToString()))
            {
                return Results.Unauthorized();
            }

            if (request.Items is null || request.Items.Count == 0)
            {
                return Results.BadRequest(new ErrorResponse("В поставке нет продуктов."));
            }

            var lines = new List<DeliveryLineCommand>();
            foreach (var item in request.Items)
            {
                if (!Enum.TryParse<Unit>(item.Unit, true, out var unit))
                {
                    return Results.BadRequest(new ErrorResponse("Единица измерения: G, Ml или Pcs."));
                }

                lines.Add(new DeliveryLineCommand(item.ProductId, item.Name, unit, item.Quantity, item.ExpiresAt, item.ShelfLifeHours));
            }

            var result = await deliveries.ReceiveAsync(
                new ReceiveDeliveryCommand(request.Id, request.Number, request.CreatedAt, lines, request.Supplier, null, true),
                cancellationToken);
            if (result.Error is not null || result.Value is null)
            {
                return Results.BadRequest(new ErrorResponse(result.Error!));
            }

            return result.Value.Created
                ? Results.Created($"/api/deliveries/{result.Value.Delivery.Id}", result.Value.Delivery)
                : Results.Ok(result.Value.Delivery);
        })
        .Docs("Поставки", "Машина приехала", "Без JWT. Заголовок X-Inbound-Key. Поставка «приехала» и в остаток не входит.")
        .Created<DeliveryView>()
        .Ok<DeliveryView>()
        .Bad()
        .RequireRateLimiting("auth")
        .AllowAnonymous();
    }

    /// <summary>
    /// Чтение склада и смены, любая роль. Сводка — только менеджер.
    /// stock — остаток по продукту. batches — партии одного продукта.
    /// stop-list — плитка всех блюд: стоп шефа, кандидат по техкарте, приоритет, мало.
    /// hot-offers — подсказка алгоритма шефу, зал её не показывает. priority — только то, что шеф подтвердил.
    /// </summary>
    public static void MapStockEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization("Staff");
        group.MapGet("/stock", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.StockAsync(cancellationToken)))
            .Docs("Склад", "Остатки", "Сумма живых партий по продукту, ближайший срок и свежесть. Просроченное и нулевое не входит.")
            .Ok<IReadOnlyList<StockItemView>>();
        group.MapGet("/stock/pending-write-offs", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.PendingWriteOffsAsync(cancellationToken)))
            .Docs("Склад", "Под списание", "Просроченные партии, которые ещё не списаны человеком. В готовку не входят.")
            .RequireAuthorization("Kitchen")
            .Ok<IReadOnlyList<BatchView>>();
        group.MapGet("/stock/{productId:guid}/uses", async (Guid productId, StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.UsesAsync(productId, cancellationToken)))
            .Docs("Склад", "Куда ушёл продукт", "Приход, заказ со столом и актом списания. Новые сверху.")
            .Ok<IReadOnlyList<ProductUseView>>();
        group.MapGet("/stock/{productId:guid}/batches", async (Guid productId, StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.BatchesAsync(productId, cancellationToken)))
            .Docs("Склад", "Партии продукта", "Вход: productId. Партии от ближайшего срока, включая уже списанные.")
            .Ok<IReadOnlyList<BatchView>>();
        group.MapGet("/stop-list", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.StopListAsync(cancellationToken)))
            .Docs("Склад", "Плитки смены", "Все блюда с рецептом. isStopped — шеф, ноль заготовки или склад не тянет на порцию. reason и stopNote — почему, stopSource Chef/Prep/Stock. isCandidate — склада на 1–5 порций, зал ещё открыт.")
            .Ok<IReadOnlyList<ShiftTile>>();
        group.MapGet("/hot-offers", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.HotOffersAsync(cancellationToken)))
            .Docs("Склад", "Подсказка алгоритма", "Блюда с коротким сроком партий. Это не список зала. Шеф подтверждает приоритет отдельно.")
            .Ok<IReadOnlyList<HotOffer>>();
        group.MapGet("/priority", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.PriorityAsync(cancellationToken)))
            .Docs("Склад", "Что предлагать", "Только блюда, где шеф включил приоритет и нет стопа. Пустой массив, если шеф ещё ничего не подтвердил.")
            .Ok<IReadOnlyList<ShiftTile>>();
        group.MapGet("/dashboard", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.DashboardAsync(cancellationToken)))
            .Docs("Склад", "Сводка менеджера", "stoppedCount — шеф, ноль заготовки и автостоп склада. Рядом партии на сутки, автосписание срока за сегодня, заказы и короткий прогноз склада.")
            .Ok<DashboardView>()
            .RequireAuthorization("Manager");
    }

    /// <summary>
    /// Заказы. Список и карточка — любая роль. Создать может официант или менеджер, кухня не может.
    /// Вход: id заказа, строки блюд, для официанта обязателен стол. connectionId нужен, чтобы SignalR ответил этому клиенту.
    /// Выход: 201 принятый заказ, 200 если этот id уже проводили, 409 отказ (стоп, лимит порций или нехватка склада) без списания, 400 кривое тело.
    /// </summary>
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/orders").RequireAuthorization("Staff");
        group.MapGet("/", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.OrdersAsync(cancellationToken)))
            .Docs("Заказы", "Журнал заказов", "Принятые и отклонённые, с номером стола и дефицитом, если отказ был по складу.")
            .Ok<IReadOnlyList<OrderView>>();
        group.MapGet("/{id:guid}", async (Guid id, StockReadModel stock, CancellationToken cancellationToken) =>
        {
            var order = await stock.OrderAsync(id, cancellationToken);
            return order is null ? Results.NotFound(new ErrorResponse("Заказ не найден")) : Results.Ok(order);
        })
        .Docs("Заказы", "Карточка заказа", "Вход: id. 404, если заказа нет.")
        .Ok<OrderView>()
        .Missing();
        group.MapPost("/", async (OrderRequest request, OrderService orders, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            if (request.Items is null || request.Items.Count == 0)
            {
                return Results.BadRequest(new ErrorResponse("В заказе нет блюд."));
            }

            var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = principal.FindFirstValue(ClaimTypes.Role);
            var placement = await orders.PlaceAsync(
                new PlaceOrderCommand(
                    request.Id,
                    userId,
                    request.ConnectionId,
                    request.Items.Select(item => new OrderLineCommand(item.DishId, item.Portions)).ToList(),
                    request.TableNumber,
                    role == nameof(UserRole.Waiter)),
                cancellationToken);
            return ToOrderResult(placement);
        })
        .Docs("Заказы", "Создать заказ", "Вход: id, стол, строки блюд. Официант без стола — 400. Стоп или порций больше лимита — 409, склад не трогаем. Иначе FEFO и минус заготовка; ноль порций ставит стоп. Повтор того же id — 200 без второго списания.")
        .Created<OrderView>()
        .Ok<OrderView>()
        .Bad()
        .Produces<OrderView>(StatusCodes.Status409Conflict)
        .RequireAuthorization("Orders");
    }

    /// <summary>
    /// Смена шефа. Роли: кухня и менеджер. Склад в килограммах здесь не трогается.
    /// stop — причина и заметка, порции в 0. resume — снять стоп, если порций больше нуля, иначе 400.
    /// portions — дельта заготовки, не приход. priority — включить или выключить «предлагать».
    /// history — кто и когда менял стоп, можно сузить одним dishId. После удачи клиентам уходит shift.updated.
    /// Выход мутаций — обновлённая плитка блюда или 400 с message.
    /// </summary>
    public static void MapShiftEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/shift").RequireAuthorization("Kitchen");
        group.MapGet("/current", async (ShiftService shift, CancellationToken cancellationToken) =>
            Results.Ok(await shift.CurrentAsync(cancellationToken)))
            .Docs("Смена", "Текущая смена", "isOpen true, пока день не закрыт. Закрытая смена гасит зал, даже если вчерашние порции ещё лежат в базе.")
            .Ok<ShiftCurrentView>();
        group.MapPost("/open", async (OpenShiftBody request, ShiftService shift, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            var dishes = request.Dishes.Select(row => new OpenShiftDish(row.DishId, row.Portions)).ToList();
            var result = await shift.OpenAsync(dishes, UserId(principal), principal.Identity?.Name ?? "шеф", cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Смена", "Открыть смену", "Вход: порции заготовки на каждое блюдо с рецептом. Ноль ставит стоп. Вчерашний стоп и приоритет снимаются.")
        .Ok<ShiftCurrentView>()
        .Bad();
        group.MapPost("/close", async (ShiftService shift, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            var result = await shift.CloseAsync(UserId(principal), principal.Identity?.Name ?? "шеф", cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Смена", "Закрыть смену", "Зал перестаёт принимать заказы. Следующее открытие задаёт заготовки заново.")
        .Ok<ShiftCurrentView>()
        .Bad();
        group.MapGet("/history", async (Guid? dishId, ShiftService shift, CancellationToken cancellationToken) =>
            Results.Ok(await shift.HistoryAsync(dishId, cancellationToken)))
            .Docs("Смена", "История стопа", "Кто, когда и почему. dishId необязателен и сужает до одного блюда. Складские движения сюда не входят.")
            .Ok<IReadOnlyList<ShiftHistoryView>>();
        group.MapPost("/dishes/{dishId:guid}/stop", async (Guid dishId, ShiftStopRequest request, ShiftService shift, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            var result = await shift.StopAsync(dishId, request.Reason, request.Note, UserId(principal), principal.Identity?.Name ?? "шеф", cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Смена", "Поставить стоп", "Вход: причина OutOfProduct, Waste, Quality, WontMakeIt или Equipment и заметка для зала. Порции становятся 0, приоритет снимается. Склад без сырья ставит стоп сам, без этой ручки.")
        .Ok<ShiftTile>()
        .Bad();
        group.MapPost("/dishes/{dishId:guid}/resume", async (Guid dishId, ShiftService shift, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            var result = await shift.ResumeAsync(dishId, principal.Identity?.Name ?? "шеф", cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Смена", "Снять стоп", "Если порций нет — 400 «сначала добавьте порции». Иначе блюдо снова открыто для зала.")
        .Ok<ShiftTile>()
        .Bad();
        group.MapPost("/dishes/{dishId:guid}/portions", async (Guid dishId, ShiftPortionsRequest request, ShiftService shift, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            var result = await shift.PortionsAsync(dishId, request.Delta, principal.Identity?.Name ?? "шеф", cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Смена", "Сдвинуть порции заготовки", "Вход: delta, не ноль. Ниже нуля нельзя. Стало 0 — стоп OutOfProduct. Было в стопе и стало больше нуля — стоп снимается. Это не приход на склад.")
        .Ok<ShiftTile>()
        .Bad();
        group.MapPost("/dishes/{dishId:guid}/priority", async (Guid dishId, ShiftPriorityRequest request, ShiftService shift, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            var result = await shift.PriorityAsync(dishId, request.On, principal.Identity?.Name ?? "шеф", cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Смена", "Приоритет для зала", "Вход: on. true — предлагать, false — убрать. Стоп и ноль порций в приоритет не ставятся.")
        .Ok<ShiftTile>()
        .Bad();
    }

    /// <summary>
    /// Акт списания со склада. Роли: кухня и менеджер.
    /// Вход: причина Expiry, Waste, Spoilage, Training или StaffMeal и строки продукта.
    /// Без batchId списывает FEFO, с batchId — только эту партию. Выход: { count } или 400.
    /// </summary>
    public static void MapWriteOffEndpoints(this WebApplication app)
    {
        app.MapPost("/api/write-offs", async (WriteOffRequest request, WriteOffService writeOffs, CancellationToken cancellationToken) =>
        {
            if (request.Items is null || request.Items.Count == 0)
            {
                return Results.BadRequest(new ErrorResponse("В акте нет позиций."));
            }

            var result = await writeOffs.ActAsync(
                request.Reason,
                request.Items.Select(item => new WriteOffLine(item.ProductId, item.Quantity, item.BatchId)).ToList(),
                cancellationToken);
            return result.Error is null ? Results.Ok(new WriteOffActResponse(result.Value)) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Списания", "Провести акт", "Вход: причина и строки. Без batchId — FEFO, с batchId — только эта партия и не больше остатка. Порции заготовки не меняются. Выход: сколько строк списано.")
        .Ok<WriteOffActResponse>()
        .Bad()
        .RequireAuthorization("Kitchen");
    }

    /// <summary>
    /// Кухня. Доска — короткие и уже вскрытые партии, без очереди заказов.
    /// Отметка позиции заказа (Queued, Cooking, Ready) оставлена для старого сценария, новый экран смены её не зовёт.
    /// Отметка партии: вскрыть и заметка. Списание брака с этой ручки новый экран не использует — для этого акт.
    /// </summary>
    public static void MapKitchenEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/kitchen").RequireAuthorization("Kitchen");
        group.MapGet("/", async (KitchenService kitchen, CancellationToken cancellationToken) =>
            Results.Ok(await kitchen.BoardAsync(cancellationToken)))
            .Docs("Кухня", "Использовать первым", "Активные партии, которые истекают за 72 часа или уже вскрыты. До 40 штук. Очереди заказов нет.")
            .Ok<KitchenBoard>();
        group.MapPost("/orders/{orderId:guid}/items/{itemId:guid}", async (
            Guid orderId,
            Guid itemId,
            KitchenDishMark request,
            KitchenService kitchen,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<KitchenStatus>(request.Status, true, out var status))
            {
                return Results.BadRequest(new ErrorResponse("Статус: Queued, Cooking, Ready или Spoiled."));
            }

            var result = await kitchen.MarkDishAsync(orderId, itemId, status, request.Note, cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Кухня", "Отметить позицию заказа", "Старый сценарий. Status: Queued, Cooking или Ready. Заметка до 300 символов. Экран смены эту ручку не вызывает.")
        .Ok<OrderView>()
        .Bad();
        group.MapPost("/batches/{batchId:guid}", async (
            Guid batchId,
            KitchenBatchMark request,
            KitchenService kitchen,
            CancellationToken cancellationToken) =>
        {
            var result = await kitchen.MarkBatchAsync(batchId, request.Opened, request.Note, request.WriteOffQuantity, cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Кухня", "Вскрыть партию", "Вход: opened и заметка. writeOffQuantity новый экран не шлёт: брак идёт актом POST /api/write-offs.")
        .Ok<KitchenBatchCard>()
        .Bad();
    }

    /// <summary>
    /// Аналитика менеджера. Списания за период, необязательный фильтр reason.
    /// Дефицит — по отказанным заказам. Прогноз недели — темп этой недели на следующую с учётом срока.
    /// Заявка на закупку возвращает QR, на склад ничего не кладёт.
    /// </summary>
    /// <summary>Деньги: закупка по людям, выручка, себестоимость и чистыми. Только менеджер.</summary>
    public static void MapReportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/reports", async (ReportService reports, CancellationToken cancellationToken) =>
            Results.Ok(await reports.BuildAsync(cancellationToken)))
            .Docs("Аналитика", "Отчёт по деньгам", "Закупка в рублях по сотрудникам и продуктам, выручка по цене меню, себестоимость проданного и чистыми. Брак и срок — отдельной суммой.")
            .Ok<MoneyReport>()
            .RequireAuthorization("Manager");
    }

    public static void MapAnalyticsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/analytics").RequireAuthorization("Manager");
        group.MapGet("/write-offs", async (DateTime? from, DateTime? to, string? reason, StockReadModel stock, CancellationToken cancellationToken) =>
        {
            var end = to ?? DateTime.UtcNow.AddDays(1);
            var start = from ?? end.AddDays(-30);
            return Results.Ok(await stock.WriteOffsAsync(DateTime.SpecifyKind(start, DateTimeKind.Utc), DateTime.SpecifyKind(end, DateTimeKind.Utc), reason, cancellationToken));
        })
        .Docs("Аналитика", "Журнал списаний", "Интервал [from, to). Если дат нет — последние 30 дней. reason фильтрует статью: Expiry, Waste, Spoilage, Training, StaffMeal.")
        .Ok<IReadOnlyList<WriteOffView>>();
        group.MapGet("/deficits", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.DeficitsAsync(cancellationToken)))
            .Docs("Аналитика", "Дефицит", "По отказанным заказам: сколько раз не хватило продукта и сколько доступно сейчас.")
            .Ok<IReadOnlyList<DeficitView>>();
        group.MapGet("/shifts", async (ShiftService shift, CancellationToken cancellationToken) =>
            Results.Ok(await shift.JournalAsync(cancellationToken)))
            .Docs("Аналитика", "Журнал смен", "Кто открыл и закрыл день кухни. Открытая смена без времени закрытия.")
            .Ok<IReadOnlyList<ShiftJournalRow>>();
        group.MapGet("/spend", async (int? days, ReportService reports, CancellationToken cancellationToken) =>
            Results.Ok(await reports.SpendAsync(days, cancellationToken)))
            .Docs("Аналитика", "Закупки за период", "days — последние сутки, включая сегодня. 0 или пусто — от первой поставки до сегодня, не длиннее 90 дней. Дни без поставок приходят с нулём. Списания — срок и акты, в рублях по цене партии.")
            .Ok<SpendReport>();
        group.MapGet("/weekly-forecast", async (StockReadModel stock, CancellationToken cancellationToken) =>
            Results.Ok(await stock.WeeklyForecastAsync(cancellationToken)))
            .Docs("Аналитика", "Прогноз на неделю", "Темп заказов с понедельника растягивается на 7 дней. Партии, которые истекут раньше дня, в этот день уже не считаются.")
            .Ok<WeeklyForecastReport>();
        group.MapPost("/purchase-request", async (PurchaseRequest request, QrIntakeService intake, CancellationToken cancellationToken) =>
        {
            var result = await intake.PurchaseRequestAsync(request.ProductIds, cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Аналитика", "QR заявки поставщику", "Вход: необязательный список productIds. Выход: номер и строка QR. Партии не создаёт.")
        .Ok<PurchaseDraft>()
        .Bad();
        group.MapPost("/purchase-lines", async (ForecastPurchaseRequest request, PurchaseService purchases, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            var result = await purchases.AddForecastAsync(request.ProductIds, UserId(principal), cancellationToken);
            return result.Error is null ? Results.Ok(new ForecastPurchaseResponse(result.Value)) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Аналитика", "Прогноз в заказ поставщику", "Кладёт дефицит недели в список заказа. Холодильник не меняется.")
        .Ok<ForecastPurchaseResponse>()
        .Bad();
    }

    /// <summary>
    /// Заказ поставщику. Читает любая роль заведения. Пишут шеф и менеджер. Склад не трогает.
    /// </summary>
    public static void MapPurchaseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/purchase-lines").RequireAuthorization("Kitchen");
        group.MapGet("/", async (bool? open, PurchaseService purchases, CancellationToken cancellationToken) =>
            Results.Ok(await purchases.ListAsync(open ?? true, cancellationToken)))
            .Docs("Поставки", "Заказ поставщику", "open=true (по умолчанию) — ещё не принятые строки. Склад не меняется.")
            .Ok<IReadOnlyList<PurchaseLineView>>();
        group.MapPost("/", async (PurchaseLineRequest request, PurchaseService purchases, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            var role = principal.FindFirstValue(ClaimTypes.Role);
            var source = role == nameof(UserRole.Kitchen) ? PurchaseSource.Chef : PurchaseSource.Manager;
            var result = await purchases.AddAsync(
                new AddPurchaseLineCommand(request.ProductId, request.Quantity, request.Note, source, UserId(principal)),
                cancellationToken);
            return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Поставки", "Добавить в заказ поставщику", "Вход: productId из каталога, количество, заметка. Холодильник не растёт.")
        .Ok<PurchaseLineView>()
        .Bad();
        group.MapDelete("/{id:guid}", async (Guid id, PurchaseService purchases, CancellationToken cancellationToken) =>
        {
            var result = await purchases.CancelAsync(id, cancellationToken);
            return result.Error is null ? Results.NoContent() : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Поставки", "Убрать строку заказа", "Только пока не приняли. 204.")
        .Produces(StatusCodes.Status204NoContent)
        .Bad();
    }

    /// <summary>
    /// Учебные кнопки менеджера: случайная поставка и случайный заказ. Идут теми же сервисами, что и боевые ручки.
    /// </summary>
    public static void MapSimulatorEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/simulator").RequireAuthorization("Manager");
        group.MapPost("/delivery", async (SimulatorService simulator, CancellationToken cancellationToken) =>
        {
            var result = await simulator.DeliveryAsync(cancellationToken);
            return result.Error is null ? Results.Ok(result.Value!.Delivery) : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .Docs("Симулятор", "Случайная поставка", "Создаёт одну поставку на существующий продукт теми же правилами, что ручная приёмка.")
        .Ok<DeliveryView>()
        .Bad();
        group.MapPost("/order", async (SimulatorService simulator, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        {
            var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return ToOrderResult(await simulator.OrderAsync(userId, cancellationToken));
        })
        .Docs("Симулятор", "Случайный заказ", "Проводит заказ от текущего пользователя без обязательного стола. 409, если блюдо в стопе или склада не хватает.")
        .Created<OrderView>()
        .Ok<OrderView>()
        .Bad()
        .Produces<OrderView>(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> SaveDish(CatalogService catalog, Guid? id, DishRequest request, CancellationToken cancellationToken)
    {
        var command = new UpsertDishCommand(
            request.Name,
            request.Ingredients.Select(line => new DishIngredientCommand(line.ProductId, line.Quantity)).ToList());
        var result = id is null
            ? await catalog.CreateDishAsync(command, cancellationToken)
            : await catalog.UpdateDishAsync(id.Value, command, cancellationToken);
        return result.Error is null ? Results.Ok(result.Value) : Results.BadRequest(new ErrorResponse(result.Error!));
    }

    private static bool InboundKeyMatches(string? expected, string provided)
    {
        if (string.IsNullOrEmpty(expected))
        {
            return false;
        }

        var left = System.Text.Encoding.UTF8.GetBytes(expected);
        var right = System.Text.Encoding.UTF8.GetBytes(provided);
        if (left.Length != right.Length)
        {
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static Guid UserId(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>400 с message, 409 с телом заказа, 201 для нового принятого, 200 для повторного id.</summary>
    private static IResult ToOrderResult(OrderPlacement placement)
    {
        if (placement.Error is not null)
        {
            return Results.BadRequest(new ErrorResponse(placement.Error));
        }

        if (placement.IsRejected)
        {
            return Results.Json(placement.Order, statusCode: StatusCodes.Status409Conflict);
        }

        return placement.Created
            ? Results.Created($"/api/orders/{placement.Order!.Id}", placement.Order)
            : Results.Ok(placement.Order);
    }

    private static AuthSessionResponse ToResponse(AuthTokens tokens) => new(
        tokens.AccessToken,
        tokens.RefreshToken,
        tokens.User.Role.ToString(),
        tokens.User.Login,
        tokens.User.Id);
}

/// <summary>Пара токенов после логина или refresh.</summary>
public sealed record AuthSessionResponse(string AccessToken, string RefreshToken, string Role, string Login, Guid UserId);

/// <summary>Учебная строка QR. На склад не влияет.</summary>
public sealed record QrSampleResponse(string Payload);
/// <summary>Сколько строк акта удалось списать.</summary>
public sealed record WriteOffActResponse(int Count);
/// <summary>Тело POST /api/auth/login.</summary>
public sealed record LoginRequest(string Login, string Password);

public sealed record RegisterRequest(string Login, string Password, string DisplayName, string Role);

public sealed record RegisterResponse(string Message);

public sealed record ChatPostRequest(string Text, Guid? RecipientId = null, bool ToSupplier = false);
public sealed record SupplyExtraItemRequest(Guid ProductId, decimal Quantity, DateTime ExpiresAt);
public sealed record SupplyExtraRequest(IReadOnlyList<SupplyExtraItemRequest>? Items);
/// <summary>Тело POST /api/auth/refresh. Access-токен сюда не подходит.</summary>
public sealed record RefreshRequest(string RefreshToken);
/// <summary>Тело создания продукта. Unit — строка G, Ml или Pcs.</summary>
public sealed record CreateProductRequest(string Name, string Unit);
/// <summary>Строка техкарты: сколько продукта уходит на одну порцию.</summary>
public sealed record DishIngredientRequest(Guid ProductId, decimal Quantity);
/// <summary>Тело создания и правки блюда.</summary>
public sealed record DishRequest(string Name, IReadOnlyList<DishIngredientRequest> Ingredients);
/// <summary>Строка ручной поставки. Срок — либо expiresAt, либо shelfLifeHours.</summary>
public sealed record DeliveryItemRequest(Guid ProductId, string Name, string Unit, decimal Quantity, DateTime? ExpiresAt, double? ShelfLifeHours);
/// <summary>Тело приёмки. Id клиент задаёт сам, повтор того же id идемпотентен.</summary>
public sealed record DeliveryRequest(Guid Id, string Number, DateTime? CreatedAt, IReadOnlyList<DeliveryItemRequest> Items, string? Supplier = null);
/// <summary>Сколько кухня взяла по одной строке поставки.</summary>
public sealed record IntakeLineRequest(Guid BatchId, decimal Quantity, DateTime? ExpiresAt = null, string? Note = null);
/// <summary>Решение кухни по поставке в приёмке.</summary>
public sealed record IntakeRequest(IReadOnlyList<IntakeLineRequest>? Lines);
/// <summary>Сырая строка QR поставки.</summary>
public sealed record QrScanRequest(string Payload);
/// <summary>Какие продукты положить в QR закупки. Пусто — берёт дефицит сам.</summary>
public sealed record PurchaseRequest(IReadOnlyList<Guid>? ProductIds);
/// <summary>Прогноз в список заказа. Пустой список — все позиции с дефицитом.</summary>
public sealed record ForecastPurchaseRequest(IReadOnlyList<Guid>? ProductIds);
/// <summary>Сколько строк прогноза легло в заказ.</summary>
public sealed record ForecastPurchaseResponse(int Count);
/// <summary>Строка заказа поставщику. Продукт уже должен быть в каталоге.</summary>
public sealed record PurchaseLineRequest(Guid ProductId, decimal Quantity, string? Note);
/// <summary>Старая отметка позиции заказа. Status: Queued, Cooking, Ready. Заметка до 300 символов.</summary>
public sealed record KitchenDishMark(string Status, string? Note);
/// <summary>Вскрытие партии. WriteOffQuantity новый экран не шлёт: брак идёт актом.</summary>
public sealed record KitchenBatchMark(bool Opened, string? Note, decimal? WriteOffQuantity);
/// <summary>Строка заказа: блюдо и число порций.</summary>
public sealed record OrderItemRequest(Guid DishId, int Portions);
/// <summary>Тело заказа. Стол обязателен только для роли официанта.</summary>
public sealed record OrderRequest(Guid Id, string? ConnectionId, IReadOnlyList<OrderItemRequest> Items, string? TableNumber);
/// <summary>Стоп шефа. Reason: OutOfProduct, Waste, Quality, WontMakeIt, Equipment.</summary>
public sealed record ShiftStopRequest(string Reason, string? Note);

public sealed record OpenShiftBody(IReadOnlyList<OpenShiftDishBody> Dishes);

public sealed record OpenShiftDishBody(Guid DishId, int Portions);
/// <summary>На сколько порций заготовки сдвинуть плитку. Отрицательное уменьшает, ноль запрещён.</summary>
public sealed record ShiftPortionsRequest(int Delta);
/// <summary>true — предлагать залу, false — убрать из приоритета.</summary>
public sealed record ShiftPriorityRequest(bool On);
/// <summary>Строка акта. BatchId пустой — списать по FEFO.</summary>
public sealed record WriteOffItemRequest(Guid ProductId, decimal Quantity, Guid? BatchId);
/// <summary>Акт списания. Reason: Expiry, Waste, Spoilage, Training, StaffMeal.</summary>
public sealed record WriteOffRequest(string Reason, IReadOnlyList<WriteOffItemRequest> Items);
