using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreManagement.Api.Dtos;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;
using StoreManagement.Api.Repositories;

namespace StoreManagement.Api.Controllers
{
    [Authorize]
    [Route("api/stock")]
    public class StockController : BaseApiController
    {
        private readonly IStockRepository _stockRepository;
        private readonly IProductRepository _productRepository;

        public StockController(IStockRepository stockRepository, IProductRepository productRepository)
        {
            _stockRepository = stockRepository;
            _productRepository = productRepository;
        }

        [HttpGet("movements")]
        public async Task<IActionResult> GetMovements([FromQuery] int? productId = null, [FromQuery] int limit = 50)
        {
            var movements = await _stockRepository.GetMovementsByUserIdAsync(CurrentUserId, productId, limit);
            return Ok(ApiResponse<IEnumerable<StockMovement>>.SuccessResult(movements, "Stock movement log retrieved."));
        }

        [HttpPost("adjust")]
        public async Task<IActionResult> AdjustStock([FromBody] ManualStockAdjustmentDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse.ErrorResult("Invalid stock adjustment data."));
            }

            var product = await _productRepository.GetByIdAsync(dto.ProductId, CurrentUserId);
            if (product == null)
            {
                return NotFound(ApiResponse.ErrorResult("Product not found."));
            }

            await _stockRepository.AdjustStockAsync(CurrentUserId, dto.ProductId, dto.QuantityChanged, dto.Reason);
            var updatedProduct = await _productRepository.GetByIdAsync(dto.ProductId, CurrentUserId);

            return Ok(ApiResponse<Product>.SuccessResult(updatedProduct!, "Stock adjusted successfully."));
        }
    }

    public class ManualStockAdjustmentDto
    {
        public int ProductId { get; set; }
        public int QuantityChanged { get; set; }
        public string Reason { get; set; } = "MANUAL_ADJUSTMENT";
    }
}
