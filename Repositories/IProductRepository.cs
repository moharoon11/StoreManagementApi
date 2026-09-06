using StoreManagement.Api.Dtos;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public interface IProductRepository
    {
        Task<PagedResponse<Product>> GetFilteredProductsAsync(int userId, ProductFilterDto filter);
        Task<Product?> GetByIdAsync(int id, int userId);
        Task<Product?> GetByNameAsync(string name, int userId);
        Task<int> CreateProductAsync(Product product);
        Task<bool> UpdateProductAsync(Product product);
        Task<bool> DeleteProductAsync(int id, int userId);
        Task<bool> UpdateStockAsync(int id, int userId, decimal newQuantity);
    }
}
