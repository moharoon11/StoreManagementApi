using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public class InvoiceSummaryDto
    {
        public int TotalTransactions { get; set; }
        public decimal TotalSale { get; set; }
        public decimal BalanceDue { get; set; }
    }

    public interface IInvoiceRepository
    {
        Task<PagedResponse<Invoice>> GetInvoiceHistoryAsync(int userId, int pageNumber = 1, int pageSize = 10, DateTime? fromDate = null, DateTime? toDate = null);
        Task<Invoice?> GetByIdAsync(int id, int userId);
        Task<InvoiceSummaryDto> GetInvoiceSummaryAsync(int userId, DateTime? fromDate = null, DateTime? toDate = null);
        Task<bool> UpdateInvoiceAsync(int id, int userId, Dtos.UpdateInvoiceDto dto);
        Task<bool> DeleteInvoiceAsync(int id, int userId);
    }
}
