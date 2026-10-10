using System.ComponentModel.DataAnnotations;

namespace StoreManagement.Api.Dtos;

public class CreateCustomerCreditInvoiceDto
{
    [Required, StringLength(150)] public string CustomerName { get; set; } = string.Empty;
    [Required, StringLength(20), RegularExpression(@"^[0-9+\-\s()]{7,20}$", ErrorMessage = "Customer mobile number is invalid")]
    public string CustomerMobileNumber { get; set; } = string.Empty;
    [Range(typeof(decimal), "0.01", "999999999999999.99")] public decimal BorrowedAmount { get; set; }
    [DataType(DataType.Date)] public DateTime? InvoiceDate { get; set; }
    public string? ProductName { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Price { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
}

public class AddCustomerCreditTransactionDto
{
    [Range(typeof(decimal), "0.01", "999999999999999.99")] public decimal Amount { get; set; }
    [DataType(DataType.Date)] public DateTime? TransactionDate { get; set; }
    public string? ProductName { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Price { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
}
