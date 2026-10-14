using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Monitoring.Core.Live;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Persistence;
using Monitoring.Infrastructure.Seeding;
using Monitoring.Infrastructure.Services;

namespace Monitoring.Infrastructure;

/// <summary>Регистрирует базу, сервисы склада и смены. Миграция и сид — отдельно при старте.</summary>
public static class DependencyInjection
{
    /// <summary>Вход: конфиг с ConnectionStrings:Postgres. Кладёт DbContext и прикладные сервисы в контейнер.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Задайте ConnectionStrings:Postgres.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        services.TryAddSingletonClock();
        services.TryAddSingleton<ILiveNotifier, NoopLiveNotifier>();
        services.AddSingleton<ExpiryWatchState>();
        services.AddScoped<StockReadModel>();
        services.AddScoped<StockBroadcaster>();
        services.AddScoped<PurchaseService>();
        services.AddScoped<DeliveryService>();
        services.AddScoped<QrIntakeService>();
        services.AddScoped<OrderService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<ExpiryProcessor>();
        services.AddScoped<SimulatorService>();
        services.AddScoped<AuthService>();
        services.AddScoped<KitchenService>();
        services.AddScoped<ShiftService>();
        services.AddScoped<WriteOffService>();
        services.AddScoped<ReportService>();
        services.AddScoped<DbSeeder>();
        return services;
    }

    /// <summary>Накатывает миграции и сид. База должна уже слушать, иначе старт падает.</summary>
    public static async Task MigrateAndSeedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(cancellationToken);
                break;
            }
            catch (Exception) when (attempt < 20)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<ExpiryProcessor>().RunOnceAsync(cancellationToken);
    }

    private static void TryAddSingletonClock(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(IClock)))
        {
            services.AddSingleton<IClock, SystemClock>();
        }
    }
}
