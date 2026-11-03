using System.Threading.Channels;
using NLog;

namespace StoreManagement.Api.Services;

public sealed class EmailQueueService : BackgroundService, IEmailQueue
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly Channel<Func<IEmailService, CancellationToken, Task>> _queue =
        Channel.CreateBounded<Func<IEmailService, CancellationToken, Task>>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly IServiceScopeFactory _scopeFactory;
    public EmailQueueService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;
    public bool Queue(Func<IEmailService, CancellationToken, Task> send)
    {
        try
        {
            var queued = _queue.Writer.TryWrite(send);
            if (!queued)
                Logger.Warn(this.MethodName() + " | Security email could not be queued.");
            return queued;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Service operation failed.");
            throw;
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var send in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await send(scope.ServiceProvider.GetRequiredService<IEmailService>(), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { Logger.Error(ex, this.MethodName() + " | A security email could not be delivered."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.Fatal(ex, this.MethodName() + " | Email queue stopped unexpectedly.");
            throw;
        }
    }
}
