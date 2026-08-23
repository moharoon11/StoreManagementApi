using Dapper;
using StoreManagement.Api.Data;
using StoreManagement.Api.Dtos;

namespace StoreManagement.Api.Repositories
{
    public class DashboardRepository : IDashboardRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public DashboardRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();

            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1).AddTicks(-1);

            var parameters = new { UserId = userId, TodayStart = todayStart, TodayEnd = todayEnd };

            // 1. Today's Sales & Invoices Count
            const string todaySalesSql = @"
                SELECT 
                    COALESCE(SUM(GrandTotal), 0) AS TodaySales,
                    COUNT(*) AS TodayInvoiceCount
                FROM Invoices
                WHERE UserId = @UserId AND CreatedAt BETWEEN @TodayStart AND @TodayEnd;";

            var todayStats = await connection.QuerySingleAsync<(decimal TodaySales, int TodayInvoiceCount)>(todaySalesSql, parameters);

            // 2. Total Products & Total Categories Count
            const string totalsSql = @"
                SELECT 
                    (SELECT COUNT(*) FROM Products WHERE UserId = @UserId) AS TotalProducts,
                    (SELECT COUNT(*) FROM Categories WHERE UserId = @UserId) AS TotalCategories;";

            var totalStats = await connection.QuerySingleAsync<(int TotalProducts, int TotalCategories)>(totalsSql, parameters);

            // 3. Low Stock Products (Stock <= 5)
            const string lowStockSql = @"
                SELECT Id, Name, StockQuantity, SellingPrice, ImageUrl
                FROM Products
                WHERE UserId = @UserId AND StockQuantity <= 5
                ORDER BY StockQuantity ASC
                LIMIT 10;";

            var lowStockProducts = (await connection.QueryAsync<ProductSummaryDto>(lowStockSql, parameters)).ToList();

            // 4. Most Sold Products
            const string mostSoldSql = @"
                SELECT 
                    Id AS ProductId,
                    Name AS ProductName,
                    SoldsCount AS TotalQuantitySold,
                    (SoldsCount * SellingPrice) AS TotalRevenue
                FROM Products
                WHERE UserId = @UserId AND SoldsCount > 0
                ORDER BY SoldsCount DESC
                LIMIT 5;";

            var mostSoldProducts = (await connection.QueryAsync<TopSoldProductDto>(mostSoldSql, parameters)).ToList();

            // 5. Recent Invoices
            const string recentInvoicesSql = @"
                SELECT Id, InvoiceNumber, GrandTotal, CreatedAt
                FROM Invoices
                WHERE UserId = @UserId
                ORDER BY CreatedAt DESC
                LIMIT 5;";

            var recentInvoices = (await connection.QueryAsync<RecentInvoiceDto>(recentInvoicesSql, parameters)).ToList();

            // 6. Stock Overview
            const string stockOverviewSql = @"
                SELECT 
                    COALESCE(SUM(StockQuantity), 0) AS TotalItemsInStock,
                    COALESCE(SUM(CASE WHEN StockQuantity <= 5 AND StockQuantity > 0 THEN 1 ELSE 0 END), 0) AS LowStockCount,
                    COALESCE(SUM(CASE WHEN StockQuantity = 0 THEN 1 ELSE 0 END), 0) AS OutOfStockCount,
                    COALESCE(SUM(StockQuantity * CostPrice), 0) AS TotalStockValueCost,
                    COALESCE(SUM(StockQuantity * SellingPrice), 0) AS TotalStockValueSelling
                FROM Products
                WHERE UserId = @UserId;";

            var stockOverview = await connection.QuerySingleAsync<StockOverviewDto>(stockOverviewSql, parameters);

            // 7. Sales Trend (Last 7 Days)
            var trendStartDate = todayStart.AddDays(-6);
            const string salesTrendSql = @"
                SELECT 
                    DATE(CreatedAt) AS DateGroup,
                    COALESCE(SUM(GrandTotal), 0) AS TotalSales
                FROM Invoices
                WHERE UserId = @UserId AND CreatedAt >= @TrendStartDate
                GROUP BY DATE(CreatedAt)
                ORDER BY DATE(CreatedAt);";

            var salesData = (await connection.QueryAsync<(DateTime DateGroup, decimal TotalSales)>(
                salesTrendSql, new { UserId = userId, TrendStartDate = trendStartDate })).ToList();

            var salesTrend = new List<SalesTrendDto>();
            for (int i = 0; i < 7; i++)
            {
                var targetDate = trendStartDate.AddDays(i);
                var saleItem = salesData.FirstOrDefault(d => d.DateGroup.Date == targetDate.Date);
                salesTrend.Add(new SalesTrendDto
                {
                    DateLabel = targetDate.ToString("ddd"),
                    TotalSales = saleItem != default ? saleItem.TotalSales : 0
                });
            }

            return new DashboardSummaryDto
            {
                TodaySales = todayStats.TodaySales,
                TodayInvoiceCount = todayStats.TodayInvoiceCount,
                TotalProducts = totalStats.TotalProducts,
                TotalCategories = totalStats.TotalCategories,
                LowStockProducts = lowStockProducts,
                MostSoldProducts = mostSoldProducts,
                RecentInvoices = recentInvoices,
                StockOverview = stockOverview,
                SalesTrend = salesTrend
            };
        }
    }
}
