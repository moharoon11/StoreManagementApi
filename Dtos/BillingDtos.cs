using System.ComponentModel.DataAnnotations;

namespace StoreManagement.Api.Dtos
{
    public class CheckoutItemDto
    {
        [Required]
        public int ProductId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1")]
        public int Quantity { get; set; }
    }

    public class CheckoutRequestDto
    {

        public string CustomerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Customer mobile number is required")]
        [StringLength(20, ErrorMessage = "Customer mobile number cannot exceed 20 characters")]
        [RegularExpression(@"^[0-9+\-\s()]{7,20}$", ErrorMessage = "Customer mobile number is invalid")]
        public string CustomerMobileNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Checkout items cannot be empty")]
        [MinLength(1, ErrorMessage = "At least one item is required for checkout")]
        public List<CheckoutItemDto> Items { get; set; } = new();
    }
}
