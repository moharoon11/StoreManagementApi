using System.Data;
using MySqlConnector;

namespace StoreManagement.Api.Data
{
    public interface IDbConnectionFactory
    {
        Task<MySqlConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
    }

    public class DbConnectionFactory : IDbConnectionFactory
    {
        private readonly MySqlDataSource _dataSource;

        public DbConnectionFactory(MySqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<MySqlConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
        {
            return await _dataSource.OpenConnectionAsync(cancellationToken);
        }
    }
}
