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
    [Route("api/products")]
    public class ProductsController : BaseApiController
    {
        private static readonly Logger Logger = LogManager.GetLogger("ProductsController");
        private readonly IProductRepository _productRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IStockRepository _stockRepository;
        private readonly ICloudinaryService _cloudinaryService;

        public ProductsController(
            IProductRepository productRepository, 
            ICategoryRepository categoryRepository,
            IStockRepository stockRepository,
            ICloudinaryService cloudinaryService)
        {
            _productRepository = productRepository;
            _categoryRepository = categoryRepository;
            _stockRepository = stockRepository;
            _cloudinaryService = cloudinaryService;
        }

        [HttpGet]
        public async Task<IActionResult> GetFiltered([FromQuery] ProductFilterDto filter)
        {
            try
            {
                Logger.Debug(this.MethodName() + " | GetFiltered started.");
                var result = await _productRepository.GetFilteredProductsAsync(CurrentUserId, filter);
                return Ok(ApiResponse<PagedResponse<Product>>.SuccessResult(result, "Products retrieved successfully."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(GetFiltered));
            }
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                Logger.Debug(this.MethodName() + " | GetById started. ProductId: {0}", id);
                var product = await _productRepository.GetByIdAsync(id, CurrentUserId);
                if (product == null)
                {
                    Logger.Debug(this.MethodName() + " | Product not found. RequestId: {0}", HttpContext.TraceIdentifier);
                    return NotFound(ApiResponse.ErrorResult("Product not found."));
                }

                return Ok(ApiResponse<Product>.SuccessResult(product, "Product details retrieved."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(GetById));
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateProductDto dto)
        {
            try
            {
                Logger.Debug(this.MethodName() + " | Create started. ProductName: {0}", dto.Name);
                if (!ModelState.IsValid)
                {
                    Logger.Debug(this.MethodName() + " | Invalid product data. RequestId: {0}", HttpContext.TraceIdentifier);
                    return BadRequest(ApiResponse.ErrorResult("Invalid product data."));
                }
                if (string.IsNullOrWhiteSpace(dto.Unit))
                {
                    Logger.Debug(this.MethodName() + " | A product unit is required. RequestId: {0}", HttpContext.TraceIdentifier);
                    return BadRequest(ApiResponse.ErrorResult("A product unit is required."));
                }

                int categoryId;

                // Option 1: Inline Category Creation if NewCategoryName provided
                if (!string.IsNullOrWhiteSpace(dto.NewCategoryName))
                {
                    var existingCategory = await _categoryRepository.GetByNameAsync(dto.NewCategoryName, CurrentUserId);
                    if (existingCategory != null)
                    {
                        categoryId = existingCategory.Id;
                    }
                    else
                    {
                        var newCat = new Category
                        {
                            UserId = CurrentUserId,
                            Name = dto.NewCategoryName.Trim()
                        };
                        categoryId = await _categoryRepository.CreateCategoryAsync(newCat);
                    }
                }
                // Option 2: Existing Category ID selected
                else if (dto.CategoryId.HasValue)
                {
                    var category = await _categoryRepository.GetByIdAsync(dto.CategoryId.Value, CurrentUserId);
                    if (category == null)
                    {
                        Logger.Debug(this.MethodName() + " | Selected category does not exist. RequestId: {0}", HttpContext.TraceIdentifier);
                        return BadRequest(ApiResponse.ErrorResult("Selected category does not exist."));
                    }
                    categoryId = dto.CategoryId.Value;
                }
                else
                {
                    Logger.Debug(this.MethodName() + " | Please select an existing category or provide a new category name. RequestId: {0}", HttpContext.TraceIdentifier);
                    return BadRequest(ApiResponse.ErrorResult("Please select an existing category or provide a new category name."));
                }

                string? imageUrl = dto.ImageUrl;
                if (dto.ImageFile != null && dto.ImageFile.Length > 0)
                {
                    imageUrl = await _cloudinaryService.UploadImageAsync(dto.ImageFile, "product_images");
                }

                var product = new Product
                {
                    UserId = CurrentUserId,
                    CategoryId = categoryId,
                    Name = dto.Name,
                    ImageUrl = imageUrl,
                    CostPrice = dto.CostPrice,
                    SellingPrice = dto.SellingPrice,
                    // Start at zero so AdjustStockAsync both applies the initial
                    // amount and records one accurate stock movement.
                    StockQuantity = 0,
                    Unit = dto.Unit.Trim()
                };

                var id = await _productRepository.CreateProductAsync(product);

                // Log initial stock movement if > 0
                if (dto.StockQuantity > 0)
                {
                    await _stockRepository.AdjustStockAsync(CurrentUserId, id, dto.StockQuantity, "STOCK_ADDED");
                }

                var createdProduct = await _productRepository.GetByIdAsync(id, CurrentUserId);
                return CreatedAtAction(nameof(GetById), new { id = id }, ApiResponse<Product>.SuccessResult(createdProduct!, "Product created successfully."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Create));
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateProductDto dto)
        {
            try
            {
                Logger.Debug(this.MethodName() + " | Update started. ProductId: {0}", id);
                if (!ModelState.IsValid)
                {
                    Logger.Debug(this.MethodName() + " | Invalid product data. RequestId: {0}", HttpContext.TraceIdentifier);
                    return BadRequest(ApiResponse.ErrorResult("Invalid product data."));
                }
                if (string.IsNullOrWhiteSpace(dto.Unit))
                {
                    Logger.Debug(this.MethodName() + " | A product unit is required. RequestId: {0}", HttpContext.TraceIdentifier);
                    return BadRequest(ApiResponse.ErrorResult("A product unit is required."));
                }

                var existingProduct = await _productRepository.GetByIdAsync(id, CurrentUserId);
                if (existingProduct == null)
                {
                    Logger.Debug(this.MethodName() + " | Product not found. RequestId: {0}", HttpContext.TraceIdentifier);
                    return NotFound(ApiResponse.ErrorResult("Product not found."));
                }

                var category = await _categoryRepository.GetByIdAsync(dto.CategoryId, CurrentUserId);
                if (category == null)
                {
                    Logger.Debug(this.MethodName() + " | Selected category does not exist. RequestId: {0}", HttpContext.TraceIdentifier);
                    return BadRequest(ApiResponse.ErrorResult("Selected category does not exist."));
                }

                string? previousImageUrl = existingProduct.ImageUrl;
                string? imageUrl = dto.ImageUrl ?? previousImageUrl;
                if (dto.ImageFile != null && dto.ImageFile.Length > 0)
                {
                    imageUrl = await _cloudinaryService.UploadImageAsync(dto.ImageFile, "product_images");
                }

                existingProduct.CategoryId = dto.CategoryId;
                existingProduct.Name = dto.Name;
                existingProduct.ImageUrl = imageUrl;
                existingProduct.CostPrice = dto.CostPrice;
                existingProduct.SellingPrice = dto.SellingPrice;
                existingProduct.Unit = dto.Unit.Trim();
            
                // Note: If stock quantity changed, log stock adjustment
                decimal quantityDifference = dto.StockQuantity - existingProduct.StockQuantity;

                if (quantityDifference != 0)
                {
                    await _stockRepository.AdjustStockAsync(CurrentUserId, id, quantityDifference, "MANUAL_ADJUSTMENT");
                }

                // Persist the final amount after the adjustment is accepted, so
                // negative stock cannot partially save an edit.
                existingProduct.StockQuantity = dto.StockQuantity;
                var updated = await _productRepository.UpdateProductAsync(existingProduct);
                if (!updated)
                {
                    Logger.Warn(this.MethodName() + " | Failed to update product. RequestId: {0}", HttpContext.TraceIdentifier);
                    return BadRequest(ApiResponse.ErrorResult("Failed to update product."));
                }

                var result = await _productRepository.GetByIdAsync(id, CurrentUserId);
                if (result != null)
                {
                    await _cloudinaryService.DeleteReplacedImageAsync(previousImageUrl, result.ImageUrl);
                }
                return Ok(ApiResponse<Product>.SuccessResult(result!, "Product updated successfully."));
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
                Logger.Debug(this.MethodName() + " | Delete started. ProductId: {0}", id);
                var product = await _productRepository.GetByIdAsync(id, CurrentUserId);
                if (product == null)
                {
                    Logger.Debug(this.MethodName() + " | Product not found. RequestId: {0}", HttpContext.TraceIdentifier);
                    return NotFound(ApiResponse.ErrorResult("Product not found."));
                }

                var deleted = await _productRepository.DeleteProductAsync(id, CurrentUserId);
                if (!deleted)
                {
                    Logger.Warn(this.MethodName() + " | Could not delete product. RequestId: {0}", HttpContext.TraceIdentifier);
                    return BadRequest(ApiResponse.ErrorResult("Could not delete product."));
                }

                await _cloudinaryService.DeleteReplacedImageAsync(product.ImageUrl, null);
                return Ok(ApiResponse.SuccessResult("Product deleted successfully."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Delete));
            }
        }

        [HttpPatch("{id:int}/stock")]
        public async Task<IActionResult> UpdateStock(int id, [FromBody] UpdateStockDto dto)
        {
            try
            {
                Logger.Debug(this.MethodName() + " | UpdateStock started. ProductId: {0}", id);
                if (!ModelState.IsValid)
                {
                    Logger.Debug(this.MethodName() + " | Invalid stock update data. RequestId: {0}", HttpContext.TraceIdentifier);
                    return BadRequest(ApiResponse.ErrorResult("Invalid stock update data."));
                }

                var product = await _productRepository.GetByIdAsync(id, CurrentUserId);
                if (product == null)
                {
                    Logger.Debug(this.MethodName() + " | Product not found. RequestId: {0}", HttpContext.TraceIdentifier);
                    return NotFound(ApiResponse.ErrorResult("Product not found."));
                }

                await _stockRepository.AdjustStockAsync(CurrentUserId, id, dto.QuantityChanged, dto.Reason);
                var updatedProduct = await _productRepository.GetByIdAsync(id, CurrentUserId);

                return Ok(ApiResponse<Product>.SuccessResult(updatedProduct!, "Product stock updated successfully."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(UpdateStock));
            }
        }
    }
}
