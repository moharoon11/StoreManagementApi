using Dapper;
using StoreManagement.Api.Data;
using StoreManagement.Api.Dtos;

namespace StoreManagement.Api.Repositories
{
    public class ReportRepository : IReportRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public ReportRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<SalesReportDto> GetSalesReportAsync(int userId, SalesReportFilterDto filter)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();

            DateTime now = DateTime.UtcNow;
            DateTime startDate;
            DateTime endDate = now;

            switch (filter.Period?.ToLower())
            {
                case "today":
                    startDate = now.Date;
                    endDate = now.Date.AddDays(1).AddTicks(-1);
                    break;
                case "week":
                    startDate = now.Date.AddDays(-(int)now.DayOfWeek);
                    endDate = now;
                    break;
                case "month":
                    startDate = new DateTime(now.Year, now.Month, 1);
                    endDate = now;
                    break;
                case "custom":
                    startDate = filter.FromDate ?? now.Date;
                    endDate = filter.ToDate ?? now;
                    break;
                default:
                    startDate = now.Date;
                    break;
            }

            var parameters = new { UserId = userId, StartDate = startDate, EndDate = endDate };

            // 1. Overall Summary
            const string summarySql = @"
                SELECT 
                    COALESCE(SUM(GrandTotal), 0) AS TotalSales,
                    COUNT(*) AS TotalInvoices
                FROM Invoices
                WHERE UserId = @UserId AND CreatedAt BETWEEN @StartDate AND @EndDate;";

            var summary = await connection.QuerySingleAsync<(decimal TotalSales, int TotalInvoices)>(summarySql, parameters);

            // 2. Total Products Sold
            const string totalItemsSql = @"
                SELECT COALESCE(SUM(ii.Quantity), 0)
                FROM InvoiceItems ii
                INNER JOIN Invoices i ON ii.InvoiceId = i.Id
                WHERE i.UserId = @UserId AND i.CreatedAt BETWEEN @StartDate AND @EndDate;";

            var totalProductsSold = await connection.ExecuteScalarAsync<decimal>(totalItemsSql, parameters);

            // 3. Top Sold Products
            const string topProductsSql = @"
                SELECT 
                    ii.ProductId,
                    ii.ProductName,
                    ii.Unit,
                    SUM(ii.Quantity) AS TotalQuantitySold,
                    SUM(ii.Total) AS TotalRevenue
                FROM InvoiceItems ii
                INNER JOIN Invoices i ON ii.InvoiceId = i.Id
                WHERE i.UserId = @UserId AND i.CreatedAt BETWEEN @StartDate AND @EndDate
                GROUP BY ii.ProductId, ii.ProductName, ii.Unit
                ORDER BY TotalQuantitySold DESC
                LIMIT 10;";

            var topProducts = (await connection.QueryAsync<TopSoldProductDto>(topProductsSql, parameters)).ToList();

            // 4. Sales by Category
            const string categorySalesSql = @"
                SELECT 
                    c.Id AS CategoryId,
                    c.Name AS CategoryName,
                    SUM(ii.Quantity) AS TotalQuantitySold,
                    SUM(ii.Total) AS TotalRevenue
                FROM InvoiceItems ii
                INNER JOIN Invoices i ON ii.InvoiceId = i.Id
                INNER JOIN Products p ON ii.ProductId = p.Id
                INNER JOIN Categories c ON p.CategoryId = c.Id
                WHERE i.UserId = @UserId AND i.CreatedAt BETWEEN @StartDate AND @EndDate
                GROUP BY c.Id, c.Name
                ORDER BY TotalRevenue DESC;";

            var categorySales = (await connection.QueryAsync<CategorySalesDto>(categorySalesSql, parameters)).ToList();

            return new SalesReportDto
            {
                TotalSales = summary.TotalSales,
                TotalInvoices = summary.TotalInvoices,
                TotalProductsSold = totalProductsSold,
                TopSoldProducts = topProducts,
                SalesByCategory = categorySales
            };
        }
    }
}
