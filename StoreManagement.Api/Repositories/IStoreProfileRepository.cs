using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public interface IStoreProfileRepository
    {
        Task<StoreProfile?> GetByUserIdAsync(int userId);
        Task<int> CreateOrUpdateProfileAsync(StoreProfile profile);
    }
}
