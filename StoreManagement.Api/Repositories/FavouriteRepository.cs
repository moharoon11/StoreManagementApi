using Dapper;
using StoreManagement.Api.Data;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public class FavouriteRepository : IFavouriteRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public FavouriteRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<bool> AddFavouriteAsync(int userId, int productId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = @"
                INSERT INTO Favourites (UserId, ProductId, CreatedAt)
                VALUES (@UserId, @ProductId, NOW())
                ON DUPLICATE KEY UPDATE CreatedAt = NOW();";
            var affected = await connection.ExecuteAsync(sql, new { UserId = userId, ProductId = productId });
            return affected > 0;
        }

        public async Task<bool> RemoveFavouriteAsync(int userId, int productId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "DELETE FROM Favourites WHERE UserId = @UserId AND ProductId = @ProductId;";
            var affected = await connection.ExecuteAsync(sql, new { UserId = userId, ProductId = productId });
            return affected > 0;
        }

        public async Task<IEnumerable<Product>> GetFavouritesByUserIdAsync(int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT 
                    p.Id, p.UserId, p.CategoryId, c.Name AS CategoryName, p.Name, p.ImageUrl, 
                    p.CostPrice, p.SellingPrice, p.StockQuantity, p.SoldsCount, p.Unit, p.CreatedAt, p.UpdatedAt,
                    1 AS IsFavourite
                FROM Favourites f
                INNER JOIN Products p ON f.ProductId = p.Id
                INNER JOIN Categories c ON p.CategoryId = c.Id
                WHERE f.UserId = @UserId
                ORDER BY f.CreatedAt DESC;";
            return await connection.QueryAsync<Product>(sql, new { UserId = userId });
        }
    }
}
