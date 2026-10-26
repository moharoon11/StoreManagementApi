using MySqlConnector;

namespace SubscriptionPlatform.Api.Data;

public sealed class ConnectionFactory(IConfiguration configuration)
{
    private readonly string connectionString = configuration.GetConnectionString("SubscriptionDatabase") ?? throw new InvalidOperationException("ConnectionStrings:SubscriptionDatabase is required.");
    public MySqlConnection Create()
    {
        return new MySqlConnection(connectionString);
    }
}
