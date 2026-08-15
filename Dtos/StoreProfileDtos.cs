using System.ComponentModel.DataAnnotations;

namespace StoreManagement.Api.Dtos
{
    public class CreateOrUpdateStoreProfileDto
    {
        [Required(ErrorMessage = "Store name is required")]
        public string StoreName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Owner name is required")]
        public string OwnerName { get; set; } = string.Empty;

        public IFormFile? LogoFile { get; set; }
        public string? ExistingLogoUrl { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? District { get; set; }
        public string? Pincode { get; set; }
        public string? Email { get; set; }
        public string? GstNumber { get; set; }
        public string? Phone { get; set; }
        public string? AlternatePhone { get; set; }
        public string? AboutUs { get; set; }
    }
}
