using System.Threading.Channels;

namespace StoreManagement.Api.Services;

public sealed class EmailQueueService : BackgroundService, IEmailQueue
{
    private readonly Channel<Func<IEmailService, CancellationToken, Task>> _queue =
        Channel.CreateBounded<Func<IEmailService, CancellationToken, Task>>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailQueueService> _logger;
    public EmailQueueService(IServiceScopeFactory scopeFactory, ILogger<EmailQueueService> logger) => (_scopeFactory, _logger) = (scopeFactory, logger);
    public bool Queue(Func<IEmailService, CancellationToken, Task> send) => _queue.Writer.TryWrite(send);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var send in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await send(scope.ServiceProvider.GetRequiredService<IEmailService>(), stoppingToken);
            }
            catch (Exception ex) { _logger.LogError(ex, "A security email could not be delivered."); }
        }
    }
}
