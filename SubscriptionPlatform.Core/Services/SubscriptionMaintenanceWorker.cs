using Dapper;
using SubscriptionPlatform.Api.Data;
using NLog;

namespace SubscriptionPlatform.Api.Services;

public sealed class SubscriptionMaintenanceWorker(ConnectionFactory connections) : BackgroundService
{
    private static readonly Logger Logger = LogManager.GetLogger("SubscriptionPlatform.Services.SubscriptionMaintenanceWorker");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Keep checking for expired trials, paid terms, grace periods, and abandoned orders.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Process expirations together so subscription and pending-payment states stay consistent.
                await using var db = connections.Create();
                await db.OpenAsync(stoppingToken);
                await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, stoppingToken);
                // Find active terms, grace periods, and unpaid orders that have passed their allowed time.
                var expired = await db.QueryAsync<(Guid id, string status)>(new CommandDefinition(
                    "SELECT id,status FROM subscriptions WHERE (status='active' AND ends_utc<=UTC_TIMESTAMP(6)) OR (status='grace' AND grace_ends_utc<=UTC_TIMESTAMP(6)) OR (status='pending_payment' AND created_utc<=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 30 MINUTE)) FOR UPDATE",
                    transaction: tx, cancellationToken: stoppingToken));
                foreach (var row in expired)
                {
                    // Remove access after the term or checkout window ends.
                    await db.ExecuteAsync(new CommandDefinition("UPDATE subscriptions SET status='expired',version=version+1 WHERE id=@id", new { id = row.id }, tx, cancellationToken: stoppingToken));
                    if (row.status == "pending_payment")
                        // Keep the payment state consistent with the expired subscription.
                        await db.ExecuteAsync(new CommandDefinition("UPDATE payments SET status='order_expired' WHERE subscription_id=@id AND status='order_pending'", new { id = row.id }, tx, cancellationToken: stoppingToken));
                    // Save why the state changed so the subscription history is explainable.
                    await db.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO subscription_audits(subscription_id,from_status,to_status,reason,at_utc) VALUES(@id,@status,'expired','period_or_order_expired',UTC_TIMESTAMP(6))",
                        new { id = row.id, row.status }, tx, cancellationToken: stoppingToken));
                }
                // Commit all expirations found during this pass.
                await tx.CommitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Stop cleanly when the application is shutting down.
                break;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"{nameof(ExecuteAsync)} | Subscription maintenance failed | Exception: {exception.Message}");
            }

            // Wait one minute before checking for more expired records.
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
