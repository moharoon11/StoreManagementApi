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
                        Unit = product.Unit,
                        SellingPrice = product.SellingPrice,
                        Total = itemTotal
                    });
                }

                var (discountType, discountValue, discountAmount) = CalculateDiscount(
                    subtotal, request.DiscountType, request.DiscountValue);
                decimal grandTotal = subtotal - discountAmount;
                decimal amountReceived = request.AmountReceived ?? (request.IsReceived ? grandTotal : 0m);
                if (amountReceived < 0m || amountReceived > grandTotal)
                    throw new InvalidOperationException("Amount received must be between zero and the discounted total.");
                bool isReceived = amountReceived >= grandTotal;
                decimal balanceDue = Math.Max(0m, grandTotal - amountReceived);
                var invoiceDate = (request.InvoiceDate ?? DateTime.UtcNow).Date;

                // 3. Generate unique invoice number
                var random = new Random();
                var invoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{random.Next(1000, 9999)}";

                // 4. Create Invoice Header
                const string insertInvoiceSql = @"
                    INSERT INTO Invoices (UserId, InvoiceNumber, CustomerName, CustomerMobileNumber, Subtotal, DiscountType, DiscountValue, DiscountAmount, GrandTotal, IsReceived, AmountReceived, BalanceDue, InvoiceDate, CreatedAt)
                    VALUES (@UserId, @InvoiceNumber, @CustomerName, @CustomerMobileNumber, @Subtotal, @DiscountType, @DiscountValue, @DiscountAmount, @GrandTotal, @IsReceived, @AmountReceived, @BalanceDue, @InvoiceDate, NOW());
                    SELECT LAST_INSERT_ID();";

                var invoiceId = await connection.ExecuteScalarAsync<int>(insertInvoiceSql, new
                {
                    UserId = userId,
                    InvoiceNumber = invoiceNumber,
                    CustomerName = request.CustomerName?.Trim() ?? "NO_NAME",
                    CustomerMobileNumber = request.CustomerMobileNumber.Trim(),
                    Subtotal = subtotal,
                    DiscountType = discountType,
                    DiscountValue = discountValue,
                    DiscountAmount = discountAmount,
                    GrandTotal = grandTotal,
                    IsReceived = isReceived,
                    AmountReceived = amountReceived,
                    BalanceDue = balanceDue,
                    InvoiceDate = invoiceDate
                }, transaction);

                // 5. Insert Invoice Items, update Product stock & log Stock Movements
                const string insertItemSql = @"
                    INSERT INTO InvoiceItems (InvoiceId, ProductId, ProductName, Quantity, Unit, SellingPrice, Total)
                    VALUES (@InvoiceId, @ProductId, @ProductName, @Quantity, @Unit, @SellingPrice, @Total);";

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

                    var product = dbProducts[item.ProductId.Value];
                    decimal previousQty = product.StockQuantity;
                    decimal newQty = previousQty - item.Quantity;

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
                    DiscountType = discountType,
                    DiscountValue = discountValue,
                    DiscountAmount = discountAmount,
                    GrandTotal = grandTotal,
                    IsReceived = isReceived,
                    AmountReceived = amountReceived,
                    BalanceDue = balanceDue,
                    InvoiceDate = invoiceDate,
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

        public async Task<Invoice> ProcessManualCheckoutAsync(int userId, ManualCheckoutRequestDto request)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                decimal subtotal = 0;
                var invoiceItems = new List<InvoiceItem>();

                foreach (var item in request.Items)
                {
                    var itemTotal = item.Rate * item.Quantity;
                    subtotal += itemTotal;

                    invoiceItems.Add(new InvoiceItem
                    {
                        ProductId = null,
                        ProductName = "Manual Item",
                        Quantity = item.Quantity,
                        SellingPrice = item.Rate,
                        Total = itemTotal
                    });
                }

                var (discountType, discountValue, discountAmount) = CalculateDiscount(
                    subtotal, request.DiscountType, request.DiscountValue);
                decimal grandTotal = subtotal - discountAmount;
                decimal amountReceived = request.AmountReceived ?? (request.IsReceived ? grandTotal : 0m);
                if (amountReceived < 0m || amountReceived > grandTotal)
                    throw new InvalidOperationException("Amount received must be between zero and the discounted total.");
                bool isReceived = amountReceived >= grandTotal;
                decimal balanceDue = Math.Max(0m, grandTotal - amountReceived);
                var invoiceDate = (request.InvoiceDate ?? DateTime.UtcNow).Date;

                var random = new Random();
                var invoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{random.Next(1000, 9999)}";

                const string insertInvoiceSql = @"
                    INSERT INTO Invoices (UserId, InvoiceNumber, CustomerName, CustomerMobileNumber, Subtotal, DiscountType, DiscountValue, DiscountAmount, GrandTotal, IsReceived, AmountReceived, BalanceDue, InvoiceDate, CreatedAt)
                    VALUES (@UserId, @InvoiceNumber, @CustomerName, @CustomerMobileNumber, @Subtotal, @DiscountType, @DiscountValue, @DiscountAmount, @GrandTotal, @IsReceived, @AmountReceived, @BalanceDue, @InvoiceDate, NOW());
                    SELECT LAST_INSERT_ID();";

                var invoiceId = await connection.ExecuteScalarAsync<int>(insertInvoiceSql, new
                {
                    UserId = userId,
                    InvoiceNumber = invoiceNumber,
                    CustomerName = request.CustomerName?.Trim() ?? "NO_NAME",
                    CustomerMobileNumber = request.CustomerMobileNumber.Trim(),
                    Subtotal = subtotal,
                    DiscountType = discountType,
                    DiscountValue = discountValue,
                    DiscountAmount = discountAmount,
                    GrandTotal = grandTotal,
                    IsReceived = isReceived,
                    AmountReceived = amountReceived,
                    BalanceDue = balanceDue,
                    InvoiceDate = invoiceDate
                }, transaction);

                const string insertItemSql = @"
                    INSERT INTO InvoiceItems (InvoiceId, ProductId, ProductName, Quantity, Unit, SellingPrice, Total)
                    VALUES (@InvoiceId, @ProductId, @ProductName, @Quantity, @Unit, @SellingPrice, @Total);";

                foreach (var item in invoiceItems)
                {
                    item.InvoiceId = invoiceId;
                    await connection.ExecuteAsync(insertItemSql, item, transaction);
                }

                const string getStoreSql = "SELECT * FROM StoreProfiles WHERE UserId = @UserId LIMIT 1;";
                var store = await connection.QuerySingleOrDefaultAsync<StoreProfile>(getStoreSql, new { UserId = userId }, transaction);

                await transaction.CommitAsync();

                return new Invoice
                {
                    Id = invoiceId,
                    UserId = userId,
                    InvoiceNumber = invoiceNumber,
                    CustomerName = request.CustomerName?.Trim() ?? "",
                    CustomerMobileNumber = request.CustomerMobileNumber.Trim(),
                    Subtotal = subtotal,
                    DiscountType = discountType,
                    DiscountValue = discountValue,
                    DiscountAmount = discountAmount,
                    GrandTotal = grandTotal,
                    IsReceived = isReceived,
                    AmountReceived = amountReceived,
                    BalanceDue = balanceDue,
                    InvoiceDate = invoiceDate,
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

        private static (string? Type, decimal Value, decimal Amount) CalculateDiscount(
            decimal subtotal, string? requestedType, decimal? requestedValue)
        {
            var value = requestedValue ?? 0m;
            var type = requestedType?.Trim().ToUpperInvariant();
            if (value == 0m && string.IsNullOrEmpty(type)) return (null, 0m, 0m);
            if (type is not ("AMOUNT" or "PERCENTAGE"))
                throw new InvalidOperationException("Select a valid discount type.");
            if (value < 0m) throw new InvalidOperationException("Discount cannot be negative.");

            var amount = type == "PERCENTAGE"
                ? subtotal * value / 100m
                : value;
            if ((type == "PERCENTAGE" && value > 100m) || amount > subtotal)
                throw new InvalidOperationException("Discount cannot exceed the subtotal.");
            return (type, value, decimal.Round(amount, 2, MidpointRounding.AwayFromZero));
        }
    }
}
