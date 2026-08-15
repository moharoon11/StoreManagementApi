using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;
using StoreManagement.Api.Repositories;

namespace StoreManagement.Api.Controllers
{
    [Authorize]
    [Route("api/favourites")]
    public class FavouritesController : BaseApiController
    {
        private readonly IFavouriteRepository _favouriteRepository;
        private readonly IProductRepository _productRepository;

        public FavouritesController(IFavouriteRepository favouriteRepository, IProductRepository productRepository)
        {
            _favouriteRepository = favouriteRepository;
            _productRepository = productRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetFavourites()
        {
            Logger.Debug("GetFavourites started.");
            var favourites = await _favouriteRepository.GetFavouritesByUserIdAsync(CurrentUserId);
            return Ok(ApiResponse<IEnumerable<Product>>.SuccessResult(favourites, "Favourite products retrieved."));
        }

        [HttpPost("{productId:int}")]
        public async Task<IActionResult> AddFavourite(int productId)
        {
            Logger.Debug("AddFavourite started. ProductId: {0}", productId);
            var product = await _productRepository.GetByIdAsync(productId, CurrentUserId);
            if (product == null)
            {
                return NotFound(ApiResponse.ErrorResult("Product not found."));
            }

            var success = await _favouriteRepository.AddFavouriteAsync(CurrentUserId, productId);
            if (!success)
            {
                return BadRequest(ApiResponse.ErrorResult("Could not add product to favourites."));
            }

            return Ok(ApiResponse.SuccessResult("Product added to favourites."));
        }

        [HttpDelete("{productId:int}")]
        public async Task<IActionResult> RemoveFavourite(int productId)
        {
            Logger.Debug("RemoveFavourite started. ProductId: {0}", productId);
            var success = await _favouriteRepository.RemoveFavouriteAsync(CurrentUserId, productId);
            if (!success)
            {
                return NotFound(ApiResponse.ErrorResult("Favourite not found or already removed."));
            }

            return Ok(ApiResponse.SuccessResult("Product removed from favourites."));
        }
    }
}
