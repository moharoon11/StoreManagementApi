using StoreManagement.Api.Dtos;

namespace StoreManagement.Api.Repositories
{
    public interface IDashboardRepository
    {
        Task<DashboardSummaryDto> GetDashboardSummaryAsync(int userId);
    }
}
