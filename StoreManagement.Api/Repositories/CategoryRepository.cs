using Dapper;
using NLog;
using StoreManagement.Api.Data;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public class CategoryRepository : ICategoryRepository
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public CategoryRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<IEnumerable<Category>> GetAllByUserIdAsync(int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "SELECT * FROM Categories WHERE UserId = @UserId ORDER BY Name ASC;";
            var categories = (await connection.QueryAsync<Category>(sql, new { UserId = userId })).AsList();
            Logger.Debug(this.MethodName() + " | Categories loaded. UserId: {0}, Count: {1}", userId, categories.Count);
            return categories;
        }

        public async Task<Category?> GetByIdAsync(int id, int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "SELECT * FROM Categories WHERE Id = @Id AND UserId = @UserId LIMIT 1;";
            var category = await connection.QuerySingleOrDefaultAsync<Category>(sql, new { Id = id, UserId = userId });
            Logger.Debug(this.MethodName() + " | Category found: {0}. UserId: {1}, CategoryId: {2}", category is not null, userId, id);
            return category;
        }

        public async Task<Category?> GetByNameAsync(string name, int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "SELECT * FROM Categories WHERE LOWER(Name) = LOWER(@Name) AND UserId = @UserId LIMIT 1;";
            var category = await connection.QuerySingleOrDefaultAsync<Category>(sql, new { Name = name, UserId = userId });
            Logger.Debug(this.MethodName() + " | Category found by name: {0}. UserId: {1}", category is not null, userId);
            return category;
        }

        public async Task<int> CreateCategoryAsync(Category category)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = @"
                INSERT INTO Categories (UserId, Name, ImageUrl, CreatedAt, UpdatedAt)
                VALUES (@UserId, @Name, @ImageUrl, NOW(), NOW());
                SELECT LAST_INSERT_ID();";
            var id = await connection.ExecuteScalarAsync<int>(sql, category);
            Logger.Info(this.MethodName() + " | Category created. UserId: {0}, CategoryId: {1}", category.UserId, id);
            return id;
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
            if (affected > 0)
                Logger.Info(this.MethodName() + " | Category updated. UserId: {0}, CategoryId: {1}", category.UserId, category.Id);
            return affected > 0;
        }

        public async Task<bool> DeleteCategoryAsync(int id, int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "DELETE FROM Categories WHERE Id = @Id AND UserId = @UserId;";
            var affected = await connection.ExecuteAsync(sql, new { Id = id, UserId = userId });
            if (affected > 0)
                Logger.Info(this.MethodName() + " | Category deleted. UserId: {0}, CategoryId: {1}", userId, id);
            return affected > 0;
        }
    }
}
