namespace StoreManagement.Api.Models
{
    public class Invoice
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerMobileNumber { get; set; } = string.Empty;
        public decimal Subtotal { get; set; }
        public decimal GrandTotal { get; set; }
        public DateTime CreatedAt { get; set; }
        
        public StoreProfile? Store { get; set; }
        public List<InvoiceItem> Items { get; set; } = new();
    }

    public class InvoiceItem
    {
        public int Id { get; set; }
        public int InvoiceId { get; set; }
        public int? ProductId { get; set; }
        public string? ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal SellingPrice { get; set; }
        public decimal Total { get; set; }
    }
}
