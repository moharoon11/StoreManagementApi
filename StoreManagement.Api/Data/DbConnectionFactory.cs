using System.Data;
using MySqlConnector;
using NLog;

namespace StoreManagement.Api.Data
{
    public interface IDbConnectionFactory
    {
        Task<MySqlConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
    }

    public class DbConnectionFactory : IDbConnectionFactory
    {
        private readonly MySqlDataSource _dataSource;
        private static readonly Logger Logger = LogManager.GetLogger("DbConnectionFactory");

        public DbConnectionFactory(MySqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<MySqlConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
        {
            Logger.Debug("Database connection opening.");
            try
            {
                var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
                Logger.Info("Database connection opened successfully.");
                return connection;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Database connection failed.");
                throw;
            }
        }
    }
}
