using System.ComponentModel.DataAnnotations;

namespace StoreManagement.Api.Dtos
{
    public class CreateCategoryDto
    {
        [Required(ErrorMessage = "Category name is required")]
        public string Name { get; set; } = string.Empty;

        public IFormFile? ImageFile { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class UpdateCategoryDto
    {
        [Required(ErrorMessage = "Category name is required")]
        public string Name { get; set; } = string.Empty;

        public IFormFile? ImageFile { get; set; }
        public string? ImageUrl { get; set; }
    }
}
