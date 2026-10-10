using StoreManagement.Api.Dtos;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories;

public interface ICustomerCreditInvoiceRepository
{
    Task<CustomerCreditInvoice> CreateAsync(int userId, CreateCustomerCreditInvoiceDto dto);
    Task<IReadOnlyList<CustomerCreditInvoice>> GetAllAsync(int userId, bool pendingOnly);
    Task<CustomerCreditInvoice?> GetByIdAsync(int id, int userId);
    Task<CustomerCreditInvoice?> AddTransactionAsync(int id, int userId, AddCustomerCreditTransactionDto dto);
    Task<bool> MarkReceivedAsync(int id, int userId);
    Task<bool> MarkTransactionReceivedAsync(int invoiceId, int transactionId, int userId);
}
