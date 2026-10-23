namespace StoreManagement.Api.Models
{
    public class StockMovement
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal PreviousQuantity { get; set; }
        public decimal QuantityChanged { get; set; }
        public decimal NewQuantity { get; set; }
        public string Unit { get; set; } = "Piece";
        public string Reason { get; set; } = string.Empty; // SALE, STOCK_ADDED, MANUAL_ADJUSTMENT, RETURN
        public DateTime CreatedAt { get; set; }
    }
}
