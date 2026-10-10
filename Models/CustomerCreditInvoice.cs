namespace StoreManagement.Api.Models;

public class CustomerCreditInvoice
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerMobileNumber { get; set; } = string.Empty;
    public decimal BorrowedAmount { get; set; }
    public decimal OutstandingBalance { get; set; }
    public DateTime InvoiceDate { get; set; }
    public bool IsReceived { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public List<CustomerCreditTransaction> Transactions { get; set; } = new();
}

public class CustomerCreditTransaction
{
    public int Id { get; set; }
    public int CustomerCreditInvoiceId { get; set; }
    public string TransactionType { get; set; } = "CREDIT";
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? ProductName { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Price { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsReceived { get; set; }
    public DateTime? ReceivedAt { get; set; }
}
