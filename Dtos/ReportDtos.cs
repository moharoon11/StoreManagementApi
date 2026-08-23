namespace StoreManagement.Api.Dtos
{
    public class SalesReportFilterDto
    {
        public string Period { get; set; } = "today"; // today, week, month, custom
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class SalesReportDto
    {
        public decimal TotalSales { get; set; }
        public int TotalInvoices { get; set; }
        public int TotalProductsSold { get; set; }
        public List<TopSoldProductDto> TopSoldProducts { get; set; } = new();
        public List<CategorySalesDto> SalesByCategory { get; set; } = new();
    }

    public class TopSoldProductDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int TotalQuantitySold { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    public class CategorySalesDto
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public int TotalQuantitySold { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    public class DashboardSummaryDto
    {
        public decimal TodaySales { get; set; }
        public int TodayInvoiceCount { get; set; }
        public int TotalProducts { get; set; }
        public int TotalCategories { get; set; }
        public List<ProductSummaryDto> LowStockProducts { get; set; } = new();
        public List<TopSoldProductDto> MostSoldProducts { get; set; } = new();
        public List<RecentInvoiceDto> RecentInvoices { get; set; } = new();
        public StockOverviewDto StockOverview { get; set; } = new();
        public List<SalesTrendDto> SalesTrend { get; set; } = new();
    }

    public class SalesTrendDto
    {
        public string DateLabel { get; set; } = string.Empty;
        public decimal TotalSales { get; set; }
    }

    public class ProductSummaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int StockQuantity { get; set; }
        public decimal SellingPrice { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class RecentInvoiceDto
    {
        public int Id { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public decimal GrandTotal { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class StockOverviewDto
    {
        public int TotalItemsInStock { get; set; }
        public int LowStockCount { get; set; }
        public int OutOfStockCount { get; set; }
        public decimal TotalStockValueCost { get; set; }
        public decimal TotalStockValueSelling { get; set; }
    }
}
