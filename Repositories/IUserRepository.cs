using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public interface IUserRepository
    {
        Task<User?> GetByUsernameAsync(string username);
        Task<User?> GetByIdAsync(int id);
        Task<int> CreateUserAsync(User user);
    }
}
