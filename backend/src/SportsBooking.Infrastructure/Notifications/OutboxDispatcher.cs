using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SportsBooking.Infrastructure.Notifications;

/// <summary>
/// The email worker (US22, docs/stack.md "Trabalho em segundo plano"). Runs inside the API, apart from any request, so a
/// slow or broken SMTP server never delays or fails a booking or a cancellation. It creates a scope per batch (the
/// DbContext is Scoped) and asks again at once while batches come back full; otherwise it waits the poll interval on the
/// injected <see cref="TimeProvider"/>. A failure of the pass itself (the database is down) is logged and retried later.
/// </summary>
internal sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        var interval = TimeSpan.FromSeconds(settings.PollIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                claimed = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogPassFailed(logger, exception);
            }

            if (claimed < settings.BatchSize)
            {
                try
                {
                    await Task.Delay(interval, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The outbox worker is turned off (Outbox:Enabled = false).")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "A pass of the outbox worker failed; it will try again.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
