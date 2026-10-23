using StoreManagement.Api.Dtos;

namespace StoreManagement.Api.Repositories
{
    public interface IReportRepository
    {
        Task<SalesReportDto> GetSalesReportAsync(int userId, SalesReportFilterDto filter);
    }
}
