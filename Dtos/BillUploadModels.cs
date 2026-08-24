using System.ComponentModel.DataAnnotations;

namespace StoreManagement.Api.Dtos
{
    public class ExtractedBillItemDto
    {
        public int? ProductId { get; set; } // Null if it's a new product
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal CostPrice { get; set; }
        public decimal SellingPrice { get; set; } // Initialized to 0 or same as cost, user will update
        public decimal TotalAmount { get; set; }
        public bool IsNewProduct { get; set; }
    }

    public class ConfirmBillRequestDto
    {
        [Required]
        public List<ConfirmBillItemDto> Items { get; set; } = new List<ConfirmBillItemDto>();
    }

    public class ConfirmBillItemDto
    {
        public int? ProductId { get; set; } // Null if creating new
        [Required]
        public string ProductName { get; set; } = string.Empty;
        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }
        [Range(0, double.MaxValue)]
        public decimal CostPrice { get; set; }
        [Range(0, double.MaxValue)]
        public decimal SellingPrice { get; set; }
        
        // If creating a new product, it needs a category
        public int? CategoryId { get; set; } 
        public string? NewCategoryName { get; set; }
    }
}
