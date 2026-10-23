using StoreManagement.Api.Models;

namespace StoreManagement.Api.Services
{
    public interface IPdfService
    {
        byte[] GenerateInvoicePdf(Invoice invoice);
    }
}
