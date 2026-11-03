using Dapper;
using NLog;
using StoreManagement.Api.Data;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public class UserRepository : IUserRepository
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public UserRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "SELECT * FROM Users WHERE Username = @Username LIMIT 1;";
            User? user = await connection.QuerySingleOrDefaultAsync<User>(sql, new { Username = username });
            Logger.Debug(this.MethodName() + " | User found by username: {0}", user is not null);
            return user;
        }

        public async Task<User?> GetByIdAsync(int id)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "SELECT * FROM Users WHERE Id = @Id LIMIT 1;";
            User? user = await connection.QuerySingleOrDefaultAsync<User>(sql, new { Id = id });
            Logger.Debug(this.MethodName() + " | User found by ID: {0}. UserId: {1}", user is not null, id);
            return user;
        }

        public async Task<int> CreateUserAsync(User user)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = @"
                INSERT INTO Users (Username, PasswordHash, CreatedAt, UpdatedAt)
                VALUES (@Username, @PasswordHash, NOW(), NOW());
                SELECT LAST_INSERT_ID();";
            int result = await connection.ExecuteScalarAsync<int>(sql, user);
            Logger.Debug(this.MethodName() + " | User row created. UserId: {0}", result);
            return result;
        }
    }
}
