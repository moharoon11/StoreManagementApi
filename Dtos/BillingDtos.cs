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
        [Required(ErrorMessage = "Checkout items cannot be empty")]
        [MinLength(1, ErrorMessage = "At least one item is required for checkout")]
        public List<CheckoutItemDto> Items { get; set; } = new();
    }
}
