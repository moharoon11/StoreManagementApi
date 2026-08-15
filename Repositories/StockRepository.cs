using Dapper;
using StoreManagement.Api.Data;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public class StockRepository : IStockRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public StockRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<IEnumerable<StockMovement>> GetMovementsByUserIdAsync(int userId, int? productId = null, int limit = 50)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();

            var whereClause = "sm.UserId = @UserId";
            if (productId.HasValue)
            {
                whereClause += " AND sm.ProductId = @ProductId";
            }

            var sql = $@"
                SELECT 
                    sm.Id, sm.UserId, sm.ProductId, p.Name AS ProductName,
                    sm.PreviousQuantity, sm.QuantityChanged, sm.NewQuantity, sm.Reason, sm.CreatedAt
                FROM StockMovements sm
                INNER JOIN Products p ON sm.ProductId = p.Id
                WHERE {whereClause}
                ORDER BY sm.CreatedAt DESC
                LIMIT @Limit;";

            return await connection.QueryAsync<StockMovement>(sql, new { UserId = userId, ProductId = productId, Limit = limit });
        }

        public async Task<bool> AdjustStockAsync(int userId, int productId, int quantityChanged, string reason)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Lock product
                const string getProductSql = "SELECT * FROM Products WHERE Id = @Id AND UserId = @UserId FOR UPDATE;";
                var product = await connection.QuerySingleOrDefaultAsync<Product>(getProductSql, new { Id = productId, UserId = userId }, transaction);

                if (product == null)
                {
                    throw new KeyNotFoundException("Product not found.");
                }

                int previousQty = product.StockQuantity;
                int newQty = previousQty + quantityChanged;

                if (newQty < 0)
                {
                    throw new InvalidOperationException($"Stock cannot be negative. Current: {previousQty}, Adjust: {quantityChanged}.");
                }

                // Update Product
                const string updateStockSql = "UPDATE Products SET StockQuantity = @NewQty, UpdatedAt = NOW() WHERE Id = @Id AND UserId = @UserId;";
                await connection.ExecuteAsync(updateStockSql, new { NewQty = newQty, Id = productId, UserId = userId }, transaction);

                // Insert Stock Movement
                const string insertMovementSql = @"
                    INSERT INTO StockMovements (UserId, ProductId, PreviousQuantity, QuantityChanged, NewQuantity, Reason, CreatedAt)
                    VALUES (@UserId, @ProductId, @PreviousQuantity, @QuantityChanged, @NewQuantity, @Reason, NOW());";

                await connection.ExecuteAsync(insertMovementSql, new
                {
                    UserId = userId,
                    ProductId = productId,
                    PreviousQuantity = previousQty,
                    QuantityChanged = quantityChanged,
                    NewQuantity = newQty,
                    Reason = reason
                }, transaction);

                await transaction.CommitAsync();
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
