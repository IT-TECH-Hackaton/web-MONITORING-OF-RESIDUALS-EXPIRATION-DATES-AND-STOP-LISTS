using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Monitoring.Api.Swagger;

/// <summary>Тело ошибки 400 и 401: одно поле message.</summary>
public sealed record ErrorResponse(string Message);

/// <summary>Ответ /health.</summary>
public sealed record HealthResponse(string Status);

public static class OpenApiSetup
{
    public static IServiceCollection AddMonitoringSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Мониторинг остатков, сроков и стоп-листа",
                Version = "v1",
                Description = """
                    Два счётчика, их нельзя смешивать.

                    Склад — партии в граммах, миллилитрах и штуках. Заказ списывает их FEFO: раньше истекает, раньше уходит. Просроченное фоновый процесс списывает статьёй Expiry.

                    Порции на смене — заготовка, её ставит шеф. Ноль порций или подтверждённый стоп закрывают блюдо для зала. Нехватка по техкарте — только кандидат: зал от неё не серый.

                    Роли: Manager видит склад и сводку. Kitchen (шеф и менеджер) ведёт смену и акты. Orders (официант и менеджер) создаёт заказ. Staff — все три, только чтение общих списков.

                    Коды: 200 уже существующий ресурс или чтение, 201 новый, 400 кривое тело, 401 нет или протух access, 403 роль не пускает, 404 нет записи, 409 заказ отклонён и склад не списан.

                    В Swagger нажмите Authorize и вставьте access-токен из POST /api/auth/login. Refresh туда не подходит.
                    """,
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Access-токен из логина, без слова Bearer: Swagger добавит его сам.",
            });

            options.DocumentFilter<TagDocumentFilter>();
            options.OperationFilter<AuthorizeOperationFilter>();
            options.SchemaFilter<StringEnumSchemaFilter>();

            foreach (var name in new[] { "Monitoring.Api.xml", "Monitoring.Infrastructure.xml", "Monitoring.Core.xml" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, name);
                if (File.Exists(path))
                {
                    options.IncludeXmlComments(path, includeControllerXmlComments: true);
                }
            }
        });
        return services;
    }

    public static WebApplication UseMonitoringSwagger(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.DocumentTitle = "Мониторинг остатков";
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
            options.DisplayRequestDuration();
            options.EnablePersistAuthorization();
            options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
        });
        return app;
    }

    public static RouteHandlerBuilder Docs(this RouteHandlerBuilder builder, string tag, string summary, string description) =>
        builder.WithTags(tag).WithMetadata(new SummaryMeta(summary), new DescriptionMeta(description));

    public static RouteHandlerBuilder Ok<T>(this RouteHandlerBuilder builder) =>
        builder.Produces<T>(StatusCodes.Status200OK);

    public static RouteHandlerBuilder Created<T>(this RouteHandlerBuilder builder) =>
        builder.Produces<T>(StatusCodes.Status201Created);

    public static RouteHandlerBuilder Bad(this RouteHandlerBuilder builder) =>
        builder.Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

    public static RouteHandlerBuilder Missing(this RouteHandlerBuilder builder) =>
        builder.Produces<ErrorResponse>(StatusCodes.Status404NotFound);
}

file sealed class SummaryMeta(string summary) : IEndpointSummaryMetadata
{
    public string Summary { get; } = summary;
}

file sealed class DescriptionMeta(string description) : IEndpointDescriptionMetadata
{
    public string Description { get; } = description;
}

file sealed class TagDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Tags =
        [
            new OpenApiTag { Name = "Вход", Description = "Без токена. Access для остальных ручек, refresh только для обновления пары." },
            new OpenApiTag { Name = "Каталог", Description = "Продукты и техкарты. Пишет менеджер, читает любая роль." },
            new OpenApiTag { Name = "Поставки", Description = "Заказ поставщику (шеф и менеджер, склад не растёт) и приход в холодильник. QR без годного сертификата партию не создаёт." },
            new OpenApiTag { Name = "Склад", Description = "Чтение остатков, плитки смены и подсказки алгоритма. Сводка — только менеджер." },
            new OpenApiTag { Name = "Заказы", Description = "Официант или менеджер. Стоп и лимит порций отклоняют заказ, склад при отказе не меняется." },
            new OpenApiTag { Name = "Смена", Description = "Шеф и менеджер. Порции заготовки, стоп и приоритет. Килограммы склада здесь не двигаются." },
            new OpenApiTag { Name = "Списания", Description = "Акт со склада. Шеф и менеджер. Это не порции заготовки." },
            new OpenApiTag { Name = "Кухня", Description = "Короткие и вскрытые партии. Очереди заказов в ответе нет." },
            new OpenApiTag { Name = "Аналитика", Description = "Журнал списаний, дефицит, прогноз недели и QR заявки. Заявка склад не пополняет." },
            new OpenApiTag { Name = "Симулятор", Description = "Учебные поставка и заказ для менеджера. Идут теми же правилами, что боевые ручки." },
            new OpenApiTag { Name = "Служебное", Description = "Проверка, что процесс жив. Токен не нужен." },
        ];
    }
}

file sealed class AuthorizeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (!metadata.OfType<IAuthorizeData>().Any())
        {
            return;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [],
            },
        ];
        Add(operation, StatusCodes.Status401Unauthorized, "Нет access-токена, он протух или вместо него прислали refresh.");
        Add(operation, StatusCodes.Status403Forbidden, "Роль не входит в политику этой ручки.");

        var policy = metadata.OfType<IAuthorizeData>().Select(item => item.Policy).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
        if (policy is not null)
        {
            operation.Description = string.IsNullOrWhiteSpace(operation.Description)
                ? $"Политика: {policy}."
                : $"{operation.Description}\n\nПолитика: {policy}.";
        }
    }

    private static void Add(OpenApiOperation operation, int status, string description)
    {
        var key = status.ToString();
        if (operation.Responses.ContainsKey(key))
        {
            return;
        }

        operation.Responses[key] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, OpenApiSchema>
                        {
                            ["message"] = new() { Type = "string" },
                        },
                    },
                },
            },
        };
    }
}

file sealed class StringEnumSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        var type = Nullable.GetUnderlyingType(context.Type) ?? context.Type;
        if (!type.IsEnum)
        {
            return;
        }

        schema.Type = "string";
        schema.Format = null;
        schema.Enum = Enum.GetNames(type).Select(name => (IOpenApiAny)new OpenApiString(name)).ToList();
    }
}
