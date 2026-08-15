using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public interface IInvoiceRepository
    {
        Task<PagedResponse<Invoice>> GetInvoiceHistoryAsync(int userId, int pageNumber = 1, int pageSize = 10, DateTime? fromDate = null, DateTime? toDate = null);
        Task<Invoice?> GetByIdAsync(int id, int userId);
    }
}
