using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public interface IFavouriteRepository
    {
        Task<bool> AddFavouriteAsync(int userId, int productId);
        Task<bool> RemoveFavouriteAsync(int userId, int productId);
        Task<IEnumerable<Product>> GetFavouritesByUserIdAsync(int userId);
    }
}
