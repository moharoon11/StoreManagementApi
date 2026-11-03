using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using StoreManagement.Api.Dtos;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;
using StoreManagement.Api.Repositories;
using StoreManagement.Api.Services;

namespace StoreManagement.Api.Controllers
{
    [Authorize]
    [Route("api/categories")]
    public class CategoriesController : BaseApiController
    {
        private static readonly Logger Logger = LogManager.GetLogger("CategoriesController");
        private readonly ICategoryRepository _categoryRepository;
        private readonly ICloudinaryService _cloudinaryService;

        public CategoriesController(ICategoryRepository categoryRepository, ICloudinaryService cloudinaryService)
        {
            _categoryRepository = categoryRepository;
            _cloudinaryService = cloudinaryService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                Logger.Debug(this.MethodName() + " | GetAll started.");
                var categories = await _categoryRepository.GetAllByUserIdAsync(CurrentUserId);
                return Ok(ApiResponse<IEnumerable<Category>>.SuccessResult(categories, "Categories retrieved."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(GetAll));
            }
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                Logger.Debug(this.MethodName() + " | GetById started. CategoryId: {0}", id);
                var category = await _categoryRepository.GetByIdAsync(id, CurrentUserId);
                if (category == null)
                {
                    return NotFound(ApiResponse.ErrorResult("Category not found."));
                }

                return Ok(ApiResponse<Category>.SuccessResult(category, "Category details."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(GetById));
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateCategoryDto dto)
        {
            try
            {
                Logger.Debug(this.MethodName() + " | Create started. CategoryName: {0}", dto.Name);
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse.ErrorResult("Invalid category data."));
                }

                var existing = await _categoryRepository.GetByNameAsync(dto.Name, CurrentUserId);
                if (existing != null)
                {
                    return BadRequest(ApiResponse.ErrorResult($"Category with name '{dto.Name}' already exists."));
                }

                string? imageUrl = dto.ImageUrl;
                if (dto.ImageFile != null && dto.ImageFile.Length > 0)
                {
                    imageUrl = await _cloudinaryService.UploadImageAsync(dto.ImageFile, "category_images");
                }

                var category = new Category
                {
                    UserId = CurrentUserId,
                    Name = dto.Name,
                    ImageUrl = imageUrl
                };

                var id = await _categoryRepository.CreateCategoryAsync(category);
                var created = await _categoryRepository.GetByIdAsync(id, CurrentUserId);

                return CreatedAtAction(nameof(GetById), new { id = id }, ApiResponse<Category>.SuccessResult(created!, "Category created successfully."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Create));
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoryDto dto)
        {
            try
            {
                Logger.Debug(this.MethodName() + " | Update started. CategoryId: {0}", id);
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse.ErrorResult("Invalid category data."));
                }

                var category = await _categoryRepository.GetByIdAsync(id, CurrentUserId);
                if (category == null)
                {
                    return NotFound(ApiResponse.ErrorResult("Category not found."));
                }

                string? previousImageUrl = category.ImageUrl;
                string? imageUrl = dto.ImageUrl ?? previousImageUrl;
                if (dto.ImageFile != null && dto.ImageFile.Length > 0)
                {
                    imageUrl = await _cloudinaryService.UploadImageAsync(dto.ImageFile, "category_images");
                }

                category.Name = dto.Name;
                category.ImageUrl = imageUrl;

                var updated = await _categoryRepository.UpdateCategoryAsync(category);
                if (!updated)
                {
                    return BadRequest(ApiResponse.ErrorResult("Failed to update category."));
                }

                var result = await _categoryRepository.GetByIdAsync(id, CurrentUserId);
                if (result != null)
                {
                    await _cloudinaryService.DeleteReplacedImageAsync(previousImageUrl, result.ImageUrl);
                }
                return Ok(ApiResponse<Category>.SuccessResult(result!, "Category updated successfully."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Update));
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                Logger.Debug(this.MethodName() + " | Delete started. CategoryId: {0}", id);
                var category = await _categoryRepository.GetByIdAsync(id, CurrentUserId);
                if (category == null)
                {
                    return NotFound(ApiResponse.ErrorResult("Category not found."));
                }

                try
                {
                    var deleted = await _categoryRepository.DeleteCategoryAsync(id, CurrentUserId);
                    if (!deleted)
                    {
                        return BadRequest(ApiResponse.ErrorResult("Could not delete category."));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex,
                        $"{this.MethodName()} | UserId: {CurrentUserId} | CategoryId: {id} | " +
                        "The category could not be deleted because it may contain active products.");
                    return BadRequest(ApiResponse.ErrorResult("Cannot delete category because it contains active products."));
                }

                await _cloudinaryService.DeleteReplacedImageAsync(category.ImageUrl, null);
                return Ok(ApiResponse.SuccessResult("Category deleted successfully."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Delete));
            }
        }
    }
}
