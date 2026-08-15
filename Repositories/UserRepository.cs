using Dapper;
using StoreManagement.Api.Data;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public UserRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "SELECT * FROM Users WHERE Username = @Username LIMIT 1;";
            return await connection.QuerySingleOrDefaultAsync<User>(sql, new { Username = username });
        }

        public async Task<User?> GetByIdAsync(int id)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "SELECT * FROM Users WHERE Id = @Id LIMIT 1;";
            return await connection.QuerySingleOrDefaultAsync<User>(sql, new { Id = id });
        }

        public async Task<int> CreateUserAsync(User user)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = @"
                INSERT INTO Users (Username, PasswordHash, CreatedAt, UpdatedAt)
                VALUES (@Username, @PasswordHash, NOW(), NOW());
                SELECT LAST_INSERT_ID();";
            return await connection.ExecuteScalarAsync<int>(sql, user);
        }
    }
}
