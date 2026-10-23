using System.Text.Json;
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
    [Route("api/inventory")]
    public class BillUploadController : BaseApiController
    {
        private static readonly Logger Logger = LogManager.GetLogger("BillUploadController");
        private readonly IGeminiVisionService _geminiVisionService;
        private readonly IProductRepository _productRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IStockRepository _stockRepository;

        public BillUploadController(
            IGeminiVisionService geminiVisionService,
            IProductRepository productRepository,
            ICategoryRepository categoryRepository,
            IStockRepository stockRepository)
        {
            _geminiVisionService = geminiVisionService;
            _productRepository = productRepository;
            _categoryRepository = categoryRepository;
            _stockRepository = stockRepository;
        }

        [HttpPost("extract-bill")]
        public async Task<IActionResult> ExtractBill(IFormFile image)
        {
            Logger.Debug("ExtractBill started.");
            if (image == null || image.Length == 0)
            {
                return BadRequest(ApiResponse.ErrorResult("Please select an image file to upload."));
            }

            var jsonResult = await _geminiVisionService.ExtractBillDataAsync(image);
            if (string.IsNullOrEmpty(jsonResult))
            {
                return BadRequest(ApiResponse.ErrorResult("Failed to extract data from the bill. Please ensure the image is clear."));
            }

            try
            {
                // Gemini sometimes wraps JSON in markdown blocks even if instructed not to. Clean it up.
                jsonResult = jsonResult.Trim();
                if (jsonResult.StartsWith("```json"))
                {
                    jsonResult = jsonResult.Substring(7);
                    if (jsonResult.EndsWith("```"))
                    {
                        jsonResult = jsonResult.Substring(0, jsonResult.Length - 3);
                    }
                }
                else if (jsonResult.StartsWith("```"))
                {
                    jsonResult = jsonResult.Substring(3);
                    if (jsonResult.EndsWith("```"))
                    {
                        jsonResult = jsonResult.Substring(0, jsonResult.Length - 3);
                    }
                }

                var extractedItems = JsonSerializer.Deserialize<List<ExtractedBillItemDto>>(jsonResult, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (extractedItems == null || !extractedItems.Any())
                {
                    return BadRequest(ApiResponse.ErrorResult("No products could be extracted from the bill."));
                }

                foreach (var item in extractedItems)
                {
                    // Smart Matching
                    var existingProduct = await _productRepository.GetByNameAsync(item.ProductName, CurrentUserId);
                    if (existingProduct != null)
                    {
                        item.ProductId = existingProduct.Id;
                        item.IsNewProduct = false;
                        item.SellingPrice = existingProduct.SellingPrice; // Pre-fill with existing if any
                        item.Unit = existingProduct.Unit;
                    }
                    else
                    {
                        item.ProductId = null;
                        item.IsNewProduct = true;
                        item.SellingPrice = item.CostPrice; // Default to CostPrice, user will change
                    }
                }

                return Ok(ApiResponse<List<ExtractedBillItemDto>>.SuccessResult(extractedItems, "Bill data extracted successfully."));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to parse JSON from Gemini.");
                return BadRequest(ApiResponse.ErrorResult("The AI service returned malformed data. Please try again with a clearer image."));
            }
        }

        [HttpPost("process-bill")]
        public async Task<IActionResult> ProcessBill([FromBody] ConfirmBillRequestDto request)
        {
            Logger.Debug("ProcessBill started.");
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse.ErrorResult("Invalid bill data."));
            }

            int successCount = 0;

            foreach (var item in request.Items)
            {
                if (item.ProductId.HasValue)
                {
                    // Existing product
                    var existingProduct = await _productRepository.GetByIdAsync(item.ProductId.Value, CurrentUserId);
                    if (existingProduct != null)
                    {
                        // Update stock and prices
                        existingProduct.CostPrice = item.CostPrice;
                        existingProduct.SellingPrice = item.SellingPrice;
                        await _productRepository.UpdateProductAsync(existingProduct);
                        await _stockRepository.AdjustStockAsync(CurrentUserId, existingProduct.Id, item.Quantity, "BILL_UPLOAD");
                        successCount++;
                    }
                }
                else
                {
                    // New product
                    int categoryId;
                    if (!string.IsNullOrWhiteSpace(item.NewCategoryName))
                    {
                        var existingCategory = await _categoryRepository.GetByNameAsync(item.NewCategoryName, CurrentUserId);
                        if (existingCategory != null)
                        {
                            categoryId = existingCategory.Id;
                        }
                        else
                        {
                            var newCat = new Category
                            {
                                UserId = CurrentUserId,
                                Name = item.NewCategoryName.Trim()
                            };
                            categoryId = await _categoryRepository.CreateCategoryAsync(newCat);
                        }
                    }
                    else if (item.CategoryId.HasValue)
                    {
                        categoryId = item.CategoryId.Value;
                    }
                    else
                    {
                        Logger.Warn($"Skipping new product {item.ProductName} because no category was provided.");
                        continue;
                    }

                    var newProduct = new Product
                    {
                        UserId = CurrentUserId,
                        CategoryId = categoryId,
                        Name = item.ProductName,
                        CostPrice = item.CostPrice,
                        SellingPrice = item.SellingPrice,
                        // AdjustStockAsync below applies and logs the opening stock.
                        StockQuantity = 0,
                        Unit = string.IsNullOrWhiteSpace(item.Unit) ? "Piece" : item.Unit.Trim()
                    };

                    var newId = await _productRepository.CreateProductAsync(newProduct);
                    await _stockRepository.AdjustStockAsync(CurrentUserId, newId, item.Quantity, "BILL_UPLOAD");
                    successCount++;
                }
            }

            return Ok(ApiResponse.SuccessResult($"{successCount} items from the bill were successfully added to inventory."));
        }
    }
}
