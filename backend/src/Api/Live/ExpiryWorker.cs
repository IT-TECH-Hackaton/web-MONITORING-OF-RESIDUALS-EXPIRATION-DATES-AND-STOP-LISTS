using Monitoring.Infrastructure.Services;

namespace Monitoring.Api.Live;

public sealed class ExpiryWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<ExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = Math.Max(5, configuration.GetValue("Expiry:IntervalSeconds", 60));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ExpiryProcessor>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Не удалось проверить сроки годности");
            }

            await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken);
        }
    }
}
