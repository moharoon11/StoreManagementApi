using Dapper;
using StoreManagement.Api.Data;
using StoreManagement.Api.Dtos;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories;

public class CustomerCreditInvoiceRepository(IDbConnectionFactory dbConnectionFactory) : ICustomerCreditInvoiceRepository
{
    public async Task<CustomerCreditInvoice> CreateAsync(int userId, CreateCustomerCreditInvoiceDto dto)
    {
        await using var connection = await dbConnectionFactory.CreateConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var invoiceDate = (dto.InvoiceDate ?? DateTime.UtcNow).Date;
        const string insertInvoice = @"INSERT INTO CustomerCreditInvoices
            (UserId, CustomerName, CustomerMobileNumber, BorrowedAmount, OutstandingBalance, InvoiceDate)
            VALUES (@UserId, @CustomerName, @CustomerMobileNumber, @BorrowedAmount, @BorrowedAmount, @InvoiceDate);
            SELECT LAST_INSERT_ID();";
        var invoiceId = await connection.ExecuteScalarAsync<int>(insertInvoice, new
        {
            UserId = userId, CustomerName = dto.CustomerName.Trim(), CustomerMobileNumber = dto.CustomerMobileNumber.Trim(),
            dto.BorrowedAmount, InvoiceDate = invoiceDate
        }, transaction);
        await InsertCreditLinesAsync(connection, transaction, invoiceId, dto.BorrowedAmount, invoiceDate,
            dto.ProductName, dto.Quantity, dto.Price, dto.Notes, dto.Items);
        await transaction.CommitAsync();
        return (await GetByIdAsync(invoiceId, userId))!;
    }

    public async Task<IReadOnlyList<CustomerCreditInvoice>> GetAllAsync(int userId, bool pendingOnly)
    {
        await using var connection = await dbConnectionFactory.CreateConnectionAsync();
        var sql = @"SELECT * FROM CustomerCreditInvoices WHERE UserId = @UserId" +
                  (pendingOnly ? " AND IsReceived = 0" : string.Empty) + " ORDER BY IsReceived, InvoiceDate DESC, Id DESC;";
        var invoices = (await connection.QueryAsync<CustomerCreditInvoice>(sql, new { UserId = userId })).ToList();
        if (invoices.Count == 0) return invoices;
        var transactions = await connection.QueryAsync<CustomerCreditTransaction>(
            "SELECT * FROM CustomerCreditTransactions WHERE CustomerCreditInvoiceId IN @Ids ORDER BY TransactionDate DESC, Id DESC;",
            new { Ids = invoices.Select(x => x.Id).ToArray() });
        var grouped = transactions.GroupBy(x => x.CustomerCreditInvoiceId).ToDictionary(x => x.Key, x => x.ToList());
        foreach (var invoice in invoices)
            invoice.Transactions = grouped.GetValueOrDefault(invoice.Id, new List<CustomerCreditTransaction>());
        return invoices;
    }

    public async Task<CustomerCreditInvoice?> GetByIdAsync(int id, int userId)
    {
        await using var connection = await dbConnectionFactory.CreateConnectionAsync();
        const string invoiceSql = "SELECT * FROM CustomerCreditInvoices WHERE Id = @Id AND UserId = @UserId LIMIT 1;";
        var invoice = await connection.QuerySingleOrDefaultAsync<CustomerCreditInvoice>(invoiceSql, new { Id = id, UserId = userId });
        if (invoice is null) return null;
        const string transactionSql = "SELECT * FROM CustomerCreditTransactions WHERE CustomerCreditInvoiceId = @Id ORDER BY TransactionDate DESC, Id DESC;";
        invoice.Transactions = (await connection.QueryAsync<CustomerCreditTransaction>(transactionSql, new { Id = id })).ToList();
        return invoice;
    }

    public async Task<CustomerCreditInvoice?> AddTransactionAsync(int id, int userId, AddCustomerCreditTransactionDto dto)
    {
        await using var connection = await dbConnectionFactory.CreateConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        const string lockSql = "SELECT * FROM CustomerCreditInvoices WHERE Id = @Id AND UserId = @UserId AND IsReceived = 0 FOR UPDATE;";
        var invoice = await connection.QuerySingleOrDefaultAsync<CustomerCreditInvoice>(lockSql, new { Id = id, UserId = userId }, transaction);
        if (invoice is null) return null;
        var date = (dto.TransactionDate ?? DateTime.UtcNow).Date;
        await InsertCreditLinesAsync(connection, transaction, id, dto.Amount, date,
            dto.ProductName, dto.Quantity, dto.Price, dto.Notes, dto.Items);
        await connection.ExecuteAsync("UPDATE CustomerCreditInvoices SET OutstandingBalance = OutstandingBalance + @Amount WHERE Id = @Id;", new { Id = id, dto.Amount }, transaction);
        await transaction.CommitAsync();
        return await GetByIdAsync(id, userId);
    }

    public async Task<bool> MarkReceivedAsync(int id, int userId)
    {
        await using var connection = await dbConnectionFactory.CreateConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        const string lockSql = "SELECT * FROM CustomerCreditInvoices WHERE Id = @Id AND UserId = @UserId AND IsReceived = 0 FOR UPDATE;";
        var invoice = await connection.QuerySingleOrDefaultAsync<CustomerCreditInvoice>(lockSql, new { Id = id, UserId = userId }, transaction);
        if (invoice is null) return false;
        if (invoice.OutstandingBalance > 0)
            await InsertTransactionAsync(connection, transaction, id, "SETTLEMENT", invoice.OutstandingBalance, DateTime.UtcNow.Date, null, null, null, "Marked received");
        await connection.ExecuteAsync(@"UPDATE CustomerCreditTransactions
            SET IsReceived = 1, ReceivedAt = UTC_TIMESTAMP()
            WHERE CustomerCreditInvoiceId = @Id AND TransactionType = 'CREDIT' AND IsReceived = 0;", new { Id = id }, transaction);
        await connection.ExecuteAsync(@"UPDATE CustomerCreditInvoices SET IsReceived = 1, OutstandingBalance = 0, ReceivedAt = UTC_TIMESTAMP() WHERE Id = @Id;", new { Id = id }, transaction);
        await transaction.CommitAsync();
        return true;
    }

    public async Task<bool> MarkTransactionReceivedAsync(int invoiceId, int transactionId, int userId)
    {
        await using var connection = await dbConnectionFactory.CreateConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        const string invoiceSql = "SELECT * FROM CustomerCreditInvoices WHERE Id = @InvoiceId AND UserId = @UserId AND IsReceived = 0 FOR UPDATE;";
        var invoice = await connection.QuerySingleOrDefaultAsync<CustomerCreditInvoice>(invoiceSql, new { InvoiceId = invoiceId, UserId = userId }, transaction);
        if (invoice is null) return false;
        const string creditSql = @"SELECT * FROM CustomerCreditTransactions
            WHERE Id = @TransactionId AND CustomerCreditInvoiceId = @InvoiceId AND TransactionType = 'CREDIT' AND IsReceived = 0 FOR UPDATE;";
        var credit = await connection.QuerySingleOrDefaultAsync<CustomerCreditTransaction>(creditSql, new { TransactionId = transactionId, InvoiceId = invoiceId }, transaction);
        if (credit is null) return false;
        await connection.ExecuteAsync("UPDATE CustomerCreditTransactions SET IsReceived = 1, ReceivedAt = UTC_TIMESTAMP() WHERE Id = @TransactionId;", new { TransactionId = transactionId }, transaction);
        await InsertTransactionAsync(connection, transaction, invoiceId, "SETTLEMENT", credit.Amount, DateTime.UtcNow.Date, null, null, null, $"Received for credit entry #{transactionId}");
        await connection.ExecuteAsync("UPDATE CustomerCreditInvoices SET OutstandingBalance = GREATEST(0, OutstandingBalance - @Amount) WHERE Id = @InvoiceId;", new { Amount = credit.Amount, InvoiceId = invoiceId }, transaction);
        await transaction.CommitAsync();
        return true;
    }

    private static async Task InsertCreditLinesAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction,
        int invoiceId, decimal totalAmount, DateTime date, string? productName, decimal? quantity, decimal? price, string? notes, IReadOnlyCollection<CreditLineDto>? items)
    {
        if (items is { Count: > 0 })
        {
            foreach (var item in items)
                await InsertTransactionAsync(connection, transaction, invoiceId, "CREDIT", item.Quantity * item.Price, date, item.ProductName, item.Quantity, item.Price, notes);
            return;
        }
        await InsertTransactionAsync(connection, transaction, invoiceId, "CREDIT", totalAmount, date, productName, quantity, price, notes);
    }

    private static Task<int> InsertTransactionAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction,
        int invoiceId, string type, decimal amount, DateTime date, string? productName, decimal? quantity, decimal? price, string? notes) =>
        connection.ExecuteAsync(@"INSERT INTO CustomerCreditTransactions
            (CustomerCreditInvoiceId, TransactionType, Amount, TransactionDate, ProductName, Quantity, Price, Notes)
            VALUES (@InvoiceId, @Type, @Amount, @Date, @ProductName, @Quantity, @Price, @Notes);",
            new { InvoiceId = invoiceId, Type = type, Amount = amount, Date = date, ProductName = Trim(productName), Quantity = quantity, Price = price, Notes = Trim(notes) }, transaction);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
