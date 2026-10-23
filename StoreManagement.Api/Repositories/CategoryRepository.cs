using Dapper;
using StoreManagement.Api.Data;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public class CategoryRepository : ICategoryRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public CategoryRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<IEnumerable<Category>> GetAllByUserIdAsync(int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "SELECT * FROM Categories WHERE UserId = @UserId ORDER BY Name ASC;";
            return await connection.QueryAsync<Category>(sql, new { UserId = userId });
        }

        public async Task<Category?> GetByIdAsync(int id, int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "SELECT * FROM Categories WHERE Id = @Id AND UserId = @UserId LIMIT 1;";
            return await connection.QuerySingleOrDefaultAsync<Category>(sql, new { Id = id, UserId = userId });
        }

        public async Task<Category?> GetByNameAsync(string name, int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "SELECT * FROM Categories WHERE LOWER(Name) = LOWER(@Name) AND UserId = @UserId LIMIT 1;";
            return await connection.QuerySingleOrDefaultAsync<Category>(sql, new { Name = name, UserId = userId });
        }

        public async Task<int> CreateCategoryAsync(Category category)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = @"
                INSERT INTO Categories (UserId, Name, ImageUrl, CreatedAt, UpdatedAt)
                VALUES (@UserId, @Name, @ImageUrl, NOW(), NOW());
                SELECT LAST_INSERT_ID();";
            return await connection.ExecuteScalarAsync<int>(sql, category);
        }

        public async Task<bool> UpdateCategoryAsync(Category category)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = @"
                UPDATE Categories 
                SET Name = @Name, 
                    ImageUrl = COALESCE(@ImageUrl, ImageUrl), 
                    UpdatedAt = NOW()
                WHERE Id = @Id AND UserId = @UserId;";
            var affected = await connection.ExecuteAsync(sql, category);
            return affected > 0;
        }

        public async Task<bool> DeleteCategoryAsync(int id, int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "DELETE FROM Categories WHERE Id = @Id AND UserId = @UserId;";
            var affected = await connection.ExecuteAsync(sql, new { Id = id, UserId = userId });
            return affected > 0;
        }
    }
}
