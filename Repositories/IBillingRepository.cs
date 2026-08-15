using StoreManagement.Api.Dtos;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public interface IBillingRepository
    {
        Task<Invoice> ProcessCheckoutAsync(int userId, CheckoutRequestDto request);
    }
}
