using MySqlConnector;
using NLog;

namespace SubscriptionPlatform.Api.Data;

public sealed class ConnectionFactory : IAsyncDisposable
{
    private static readonly Logger Logger = LogManager.GetLogger("SubscriptionPlatform.Data.ConnectionFactory");
    private readonly MySqlDataSource dataSource;

    public ConnectionFactory(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SubscriptionDatabase")
            ?? throw new InvalidOperationException("ConnectionStrings:SubscriptionDatabase is required.");
        dataSource = new MySqlDataSource(connectionString);
    }

    public async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            Logger.Debug(this.MethodName() + " | Subscription database connection opened.");
            return connection;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Subscription database connection failed.");
            throw;
        }
    }

    public ValueTask DisposeAsync() => dataSource.DisposeAsync();
}
