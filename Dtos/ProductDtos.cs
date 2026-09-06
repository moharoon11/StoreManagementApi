using System.ComponentModel.DataAnnotations;

namespace StoreManagement.Api.Dtos
{
    public class CreateProductDto
    {
        [Required(ErrorMessage = "Product name is required")]
        public string Name { get; set; } = string.Empty;

        public int? CategoryId { get; set; }
        public string? NewCategoryName { get; set; }

        public decimal CostPrice { get; set; }
        
        [Range(0.01, double.MaxValue, ErrorMessage = "Selling price must be greater than zero")]
        public decimal SellingPrice { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Stock quantity cannot be negative")]
        public decimal StockQuantity { get; set; }

        [Required, StringLength(20)]
        public string Unit { get; set; } = "Piece";

        public IFormFile? ImageFile { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class UpdateProductDto
    {
        [Required(ErrorMessage = "Product name is required")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Category ID is required")]
        public int CategoryId { get; set; }

        public decimal CostPrice { get; set; }
        public decimal SellingPrice { get; set; }
        [Range(0, double.MaxValue, ErrorMessage = "Stock quantity cannot be negative")]
        public decimal StockQuantity { get; set; }

        [Required, StringLength(20)]
        public string Unit { get; set; } = "Piece";

        public IFormFile? ImageFile { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class UpdateStockDto
    {
        [Required]
        public decimal QuantityChanged { get; set; }

        [Required]
        public string Reason { get; set; } = "MANUAL_ADJUSTMENT"; // STOCK_ADDED, MANUAL_ADJUSTMENT, RETURN
    }

    public class ProductFilterDto
    {
        public string? SearchTerm { get; set; }
        public int? CategoryId { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public bool? IsFavourite { get; set; }
        public bool? SortByMostSold { get; set; }

        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
