namespace StoreManagement.Api.Models
{
    public class StoreProfile
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string StoreName { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? District { get; set; }
        public string? Pincode { get; set; }
        public string? Email { get; set; }
        public string? GstNumber { get; set; }
        public string? Phone { get; set; }
        public string? AlternatePhone { get; set; }
        public string? AboutUs { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
