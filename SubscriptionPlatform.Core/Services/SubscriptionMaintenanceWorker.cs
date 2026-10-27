using Dapper;
using SubscriptionPlatform.Api.Data;
using NLog;

namespace SubscriptionPlatform.Api.Services;

public sealed class SubscriptionMaintenanceWorker(ConnectionFactory connections) : BackgroundService
{
    private static readonly Logger Logger = LogManager.GetLogger("SubscriptionPlatform.Services.SubscriptionMaintenanceWorker");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var db = connections.Create();
                await db.OpenAsync(stoppingToken);
                await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, stoppingToken);
                var expired = await db.QueryAsync<(Guid id, string status)>(new CommandDefinition(
                    "SELECT id,status FROM subscriptions WHERE (status='active' AND ends_utc<=UTC_TIMESTAMP(6)) OR (status='grace' AND grace_ends_utc<=UTC_TIMESTAMP(6)) OR (status='pending_payment' AND created_utc<=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 30 MINUTE)) FOR UPDATE",
                    transaction: tx, cancellationToken: stoppingToken));
                foreach (var row in expired)
                {
                    await db.ExecuteAsync(new CommandDefinition("UPDATE subscriptions SET status='expired',version=version+1 WHERE id=@id", new { id = row.id }, tx, cancellationToken: stoppingToken));
                    if (row.status == "pending_payment")
                        await db.ExecuteAsync(new CommandDefinition("UPDATE payments SET status='order_expired' WHERE subscription_id=@id AND status='order_pending'", new { id = row.id }, tx, cancellationToken: stoppingToken));
                    await db.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO subscription_audits(subscription_id,from_status,to_status,reason,at_utc) VALUES(@id,@status,'expired','period_or_order_expired',UTC_TIMESTAMP(6))",
                        new { id = row.id, row.status }, tx, cancellationToken: stoppingToken));
                }
                await tx.CommitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"{nameof(ExecuteAsync)} | Subscription maintenance failed | Exception: {exception.Message}");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
