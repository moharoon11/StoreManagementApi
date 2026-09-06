using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public interface IStockRepository
    {
        Task<IEnumerable<StockMovement>> GetMovementsByUserIdAsync(int userId, int? productId = null, int limit = 50);
        Task<bool> AdjustStockAsync(int userId, int productId, decimal quantityChanged, string reason);
    }
}
