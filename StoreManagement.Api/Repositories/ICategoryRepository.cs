using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public interface ICategoryRepository
    {
        Task<IEnumerable<Category>> GetAllByUserIdAsync(int userId);
        Task<Category?> GetByIdAsync(int id, int userId);
        Task<Category?> GetByNameAsync(string name, int userId);
        Task<int> CreateCategoryAsync(Category category);
        Task<bool> UpdateCategoryAsync(Category category);
        Task<bool> DeleteCategoryAsync(int id, int userId);
    }
}
