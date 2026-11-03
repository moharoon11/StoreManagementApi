using System.Text;
using Dapper;
using NLog;
using StoreManagement.Api.Data;
using StoreManagement.Api.Dtos;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public ProductRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<PagedResponse<Product>> GetFilteredProductsAsync(int userId, ProductFilterDto filter)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();

            var whereClauses = new List<string> { "p.UserId = @UserId" };
            var parameters = new DynamicParameters();
            parameters.Add("UserId", userId);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                whereClauses.Add("(p.Name LIKE @SearchTerm OR c.Name LIKE @SearchTerm)");
                parameters.Add("SearchTerm", $"%{filter.SearchTerm}%");
            }

            if (filter.CategoryId.HasValue)
            {
                whereClauses.Add("p.CategoryId = @CategoryId");
                parameters.Add("CategoryId", filter.CategoryId.Value);
            }

            if (filter.MinPrice.HasValue)
            {
                whereClauses.Add("p.SellingPrice >= @MinPrice");
                parameters.Add("MinPrice", filter.MinPrice.Value);
            }

            if (filter.MaxPrice.HasValue)
            {
                whereClauses.Add("p.SellingPrice <= @MaxPrice");
                parameters.Add("MaxPrice", filter.MaxPrice.Value);
            }

            if (filter.IsFavourite.HasValue && filter.IsFavourite.Value)
            {
                whereClauses.Add("f.UserId IS NOT NULL");
            }

            var whereSql = string.Join(" AND ", whereClauses);

            // Count total query
            var countSql = $@"
                SELECT COUNT(*) 
                FROM Products p
                INNER JOIN Categories c ON p.CategoryId = c.Id
                LEFT JOIN Favourites f ON p.Id = f.ProductId AND f.UserId = p.UserId
                WHERE {whereSql};";

            var totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);

            // Sorting
            string orderBy = "p.CreatedAt DESC";
            if (filter.SortByMostSold.HasValue && filter.SortByMostSold.Value)
            {
                orderBy = "p.SoldsCount DESC, p.Name ASC";
            }

            // Pagination params
            int offset = (filter.PageNumber - 1) * filter.PageSize;
            parameters.Add("Offset", offset);
            parameters.Add("PageSize", filter.PageSize);

            // Query items
            var itemsSql = $@"
                SELECT 
                    p.Id, p.UserId, p.CategoryId, c.Name AS CategoryName, p.Name, p.ImageUrl, 
                    p.CostPrice, p.SellingPrice, p.StockQuantity, p.SoldsCount, p.Unit, p.CreatedAt, p.UpdatedAt,
                    CASE WHEN f.UserId IS NOT NULL THEN 1 ELSE 0 END AS IsFavourite
                FROM Products p
                INNER JOIN Categories c ON p.CategoryId = c.Id
                LEFT JOIN Favourites f ON p.Id = f.ProductId AND f.UserId = p.UserId
                WHERE {whereSql}
                ORDER BY {orderBy}
                LIMIT @PageSize OFFSET @Offset;";

            var items = await connection.QueryAsync<Product>(itemsSql, parameters);

            return new PagedResponse<Product>(items, filter.PageNumber, filter.PageSize, totalCount);
        }

        public async Task<Product?> GetByIdAsync(int id, int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT 
                    p.Id, p.UserId, p.CategoryId, c.Name AS CategoryName, p.Name, p.ImageUrl, 
                    p.CostPrice, p.SellingPrice, p.StockQuantity, p.SoldsCount, p.Unit, p.CreatedAt, p.UpdatedAt,
                    CASE WHEN f.UserId IS NOT NULL THEN 1 ELSE 0 END AS IsFavourite
                FROM Products p
                INNER JOIN Categories c ON p.CategoryId = c.Id
                LEFT JOIN Favourites f ON p.Id = f.ProductId AND f.UserId = p.UserId
                WHERE p.Id = @Id AND p.UserId = @UserId 
                LIMIT 1;";
            return await connection.QuerySingleOrDefaultAsync<Product>(sql, new { Id = id, UserId = userId });
        }

        public async Task<Product?> GetByNameAsync(string name, int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = @"
                SELECT 
                    p.Id, p.UserId, p.CategoryId, c.Name AS CategoryName, p.Name, p.ImageUrl, 
                    p.CostPrice, p.SellingPrice, p.StockQuantity, p.SoldsCount, p.Unit, p.CreatedAt, p.UpdatedAt,
                    CASE WHEN f.UserId IS NOT NULL THEN 1 ELSE 0 END AS IsFavourite
                FROM Products p
                INNER JOIN Categories c ON p.CategoryId = c.Id
                LEFT JOIN Favourites f ON p.Id = f.ProductId AND f.UserId = p.UserId
                WHERE LOWER(p.Name) = LOWER(@Name) AND p.UserId = @UserId 
                LIMIT 1;";
            return await connection.QuerySingleOrDefaultAsync<Product>(sql, new { Name = name, UserId = userId });
        }

        public async Task<int> CreateProductAsync(Product product)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = @"
                INSERT INTO Products 
                    (UserId, CategoryId, Name, ImageUrl, CostPrice, SellingPrice, StockQuantity, SoldsCount, Unit, CreatedAt, UpdatedAt)
                VALUES 
                    (@UserId, @CategoryId, @Name, @ImageUrl, @CostPrice, @SellingPrice, @StockQuantity, 0, @Unit, NOW(), NOW());
                SELECT LAST_INSERT_ID();";
            var id = await connection.ExecuteScalarAsync<int>(sql, product);
            Logger.Info(this.MethodName() + " | Product created. UserId: {0}, ProductId: {1}", product.UserId, id);
            return id;
        }

        public async Task<bool> UpdateProductAsync(Product product)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = @"
                UPDATE Products 
                SET CategoryId = @CategoryId,
                    Name = @Name,
                    ImageUrl = COALESCE(@ImageUrl, ImageUrl),
                    CostPrice = @CostPrice,
                    SellingPrice = @SellingPrice,
                    StockQuantity = @StockQuantity,
                    Unit = @Unit,
                    UpdatedAt = NOW()
                WHERE Id = @Id AND UserId = @UserId;";
            var affected = await connection.ExecuteAsync(sql, product);
            if (affected > 0)
                Logger.Info(this.MethodName() + " | Product updated. UserId: {0}, ProductId: {1}", product.UserId, product.Id);
            return affected > 0;
        }

        public async Task<bool> DeleteProductAsync(int id, int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "DELETE FROM Products WHERE Id = @Id AND UserId = @UserId;";
            var affected = await connection.ExecuteAsync(sql, new { Id = id, UserId = userId });
            if (affected > 0)
                Logger.Info(this.MethodName() + " | Product deleted. UserId: {0}, ProductId: {1}", userId, id);
            return affected > 0;
        }

        public async Task<bool> UpdateStockAsync(int id, int userId, decimal newQuantity)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "UPDATE Products SET StockQuantity = @NewQuantity, UpdatedAt = NOW() WHERE Id = @Id AND UserId = @UserId;";
            var affected = await connection.ExecuteAsync(sql, new { Id = id, UserId = userId, NewQuantity = newQuantity });
            if (affected > 0)
                Logger.Info(this.MethodName() + " | Product stock set. UserId: {0}, ProductId: {1}, Quantity: {2}", userId, id, newQuantity);
            return affected > 0;
        }
    }
}
