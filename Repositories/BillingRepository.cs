using Dapper;
using StoreManagement.Api.Data;
using StoreManagement.Api.Dtos;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public class BillingRepository : IBillingRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public BillingRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<Invoice> ProcessCheckoutAsync(int userId, CheckoutRequestDto request)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // 1. Fetch requested products with FOR UPDATE lock
                var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
                const string getProductsSql = @"
                    SELECT * FROM Products 
                    WHERE Id IN @ProductIds AND UserId = @UserId 
                    FOR UPDATE;";

                var dbProducts = (await connection.QueryAsync<Product>(getProductsSql, new { ProductIds = productIds, UserId = userId }, transaction)).ToDictionary(p => p.Id);

                // 2. Validate product availability and stock
                decimal subtotal = 0;
                var invoiceItems = new List<InvoiceItem>();

                foreach (var item in request.Items)
                {
                    if (!dbProducts.TryGetValue(item.ProductId, out var product))
                    {
                        throw new InvalidOperationException($"Product with ID {item.ProductId} was not found.");
                    }

                    if (product.StockQuantity < item.Quantity)
                    {
                        throw new InvalidOperationException($"Insufficient stock for '{product.Name}'. Available: {product.StockQuantity}, Requested: {item.Quantity}.");
                    }

                    var itemTotal = product.SellingPrice * item.Quantity;
                    subtotal += itemTotal;

                    invoiceItems.Add(new InvoiceItem
                    {
                        ProductId = product.Id,
                        ProductName = product.Name,
                        Quantity = item.Quantity,
                        SellingPrice = product.SellingPrice,
                        Total = itemTotal
                    });
                }

                decimal grandTotal = subtotal; // Can add tax logic if needed, default subtotal = grandtotal

                // 3. Generate unique invoice number
                var random = new Random();
                var invoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{random.Next(1000, 9999)}";

                // 4. Create Invoice Header
                const string insertInvoiceSql = @"
                    INSERT INTO Invoices (UserId, InvoiceNumber, CustomerName, CustomerMobileNumber, Subtotal, GrandTotal, CreatedAt)
                    VALUES (@UserId, @InvoiceNumber, @CustomerName, @CustomerMobileNumber, @Subtotal, @GrandTotal, NOW());
                    SELECT LAST_INSERT_ID();";

                var invoiceId = await connection.ExecuteScalarAsync<int>(insertInvoiceSql, new
                {
                    UserId = userId,
                    InvoiceNumber = invoiceNumber,
                    CustomerName = request.CustomerName?.Trim() ?? "NO_NAME",
                    CustomerMobileNumber = request.CustomerMobileNumber.Trim(),
                    Subtotal = subtotal,
                    GrandTotal = grandTotal
                }, transaction);

                // 5. Insert Invoice Items, update Product stock & log Stock Movements
                const string insertItemSql = @"
                    INSERT INTO InvoiceItems (InvoiceId, ProductId, ProductName, Quantity, SellingPrice, Total)
                    VALUES (@InvoiceId, @ProductId, @ProductName, @Quantity, @SellingPrice, @Total);";

                const string updateStockSql = @"
                    UPDATE Products 
                    SET StockQuantity = StockQuantity - @Quantity, 
                        SoldsCount = SoldsCount + @Quantity, 
                        UpdatedAt = NOW() 
                    WHERE Id = @ProductId AND UserId = @UserId;";

                const string insertStockMovementSql = @"
                    INSERT INTO StockMovements (UserId, ProductId, PreviousQuantity, QuantityChanged, NewQuantity, Reason, CreatedAt)
                    VALUES (@UserId, @ProductId, @PreviousQuantity, @QuantityChanged, @NewQuantity, 'SALE', NOW());";

                foreach (var item in invoiceItems)
                {
                    item.InvoiceId = invoiceId;
                    await connection.ExecuteAsync(insertItemSql, item, transaction);

                    var product = dbProducts[item.ProductId];
                    int previousQty = product.StockQuantity;
                    int newQty = previousQty - item.Quantity;

                    await connection.ExecuteAsync(updateStockSql, new
                    {
                        Quantity = item.Quantity,
                        ProductId = item.ProductId,
                        UserId = userId
                    }, transaction);

                    await connection.ExecuteAsync(insertStockMovementSql, new
                    {
                        UserId = userId,
                        ProductId = item.ProductId,
                        PreviousQuantity = previousQty,
                        QuantityChanged = -item.Quantity,
                        NewQuantity = newQty
                    }, transaction);
                }

                // 6. Fetch store profile for returning complete invoice detail
                const string getStoreSql = "SELECT * FROM StoreProfiles WHERE UserId = @UserId LIMIT 1;";
                var store = await connection.QuerySingleOrDefaultAsync<StoreProfile>(getStoreSql, new { UserId = userId }, transaction);

                await transaction.CommitAsync();

                return new Invoice
                {
                    Id = invoiceId,
                    UserId = userId,
                    InvoiceNumber = invoiceNumber,
                    CustomerName = request.CustomerName.Trim(),
                    CustomerMobileNumber = request.CustomerMobileNumber.Trim(),
                    Subtotal = subtotal,
                    GrandTotal = grandTotal,
                    CreatedAt = DateTime.UtcNow,
                    Store = store,
                    Items = invoiceItems
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
