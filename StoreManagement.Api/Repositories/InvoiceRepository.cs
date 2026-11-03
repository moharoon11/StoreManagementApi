using Dapper;
using NLog;
using StoreManagement.Api.Data;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public class InvoiceRepository : IInvoiceRepository
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public InvoiceRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<PagedResponse<Invoice>> GetInvoiceHistoryAsync(int userId, int pageNumber = 1, int pageSize = 10, DateTime? fromDate = null, DateTime? toDate = null)
        {
            try
            {
                using var connection = await _dbConnectionFactory.CreateConnectionAsync();

                var whereClauses = new List<string> { "UserId = @UserId" };
                var parameters = new DynamicParameters();
                parameters.Add("UserId", userId);

                if (fromDate.HasValue)
                {
                    whereClauses.Add("CreatedAt >= @FromDate");
                    parameters.Add("FromDate", fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    whereClauses.Add("CreatedAt <= @ToDate");
                    parameters.Add("ToDate", toDate.Value);
                }

                var whereSql = string.Join(" AND ", whereClauses);

                const string countSql = "SELECT COUNT(*) FROM Invoices WHERE {0};";
                var totalCount = await connection.ExecuteScalarAsync<int>(string.Format(countSql, whereSql), parameters);

                int offset = (pageNumber - 1) * pageSize;
                parameters.Add("Offset", offset);
                parameters.Add("PageSize", pageSize);

                var invoicesSql = $@"
                SELECT * FROM Invoices 
                WHERE {whereSql} 
                ORDER BY CreatedAt DESC 
                LIMIT @PageSize OFFSET @Offset;";

                var invoices = (await connection.QueryAsync<Invoice>(invoicesSql, parameters)).ToList();

                if (invoices.Any())
                {
                    var invoiceIds = invoices.Select(i => i.Id).ToList();
                    const string itemsSql = "SELECT * FROM InvoiceItems WHERE InvoiceId IN @InvoiceIds;";
                    var itemsMap = (await connection.QueryAsync<InvoiceItem>(itemsSql, new { InvoiceIds = invoiceIds }))
                        .GroupBy(i => i.InvoiceId)
                        .ToDictionary(g => g.Key, g => g.ToList());

                    foreach (var inv in invoices)
                    {
                        if (itemsMap.TryGetValue(inv.Id, out var items))
                        {
                            inv.Items = items;
                        }
                    }
                }

                var page = new PagedResponse<Invoice>(invoices, pageNumber, pageSize, totalCount);
                Logger.Debug(this.MethodName() + " | Invoice history loaded. UserId: {0}, Returned: {1}, Total: {2}", userId, invoices.Count, totalCount);
                return page;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
                throw;
            }
        }

        public async Task<Invoice?> GetByIdAsync(int id, int userId)
        {
            try
            {
                using var connection = await _dbConnectionFactory.CreateConnectionAsync();

                string invoiceSql = userId > 0
                    ? "SELECT * FROM Invoices WHERE Id = @Id AND UserId = @UserId LIMIT 1;"
                    : "SELECT * FROM Invoices WHERE Id = @Id LIMIT 1;";

                var invoice = await connection.QuerySingleOrDefaultAsync<Invoice>(invoiceSql, new { Id = id, UserId = userId });

                if (invoice == null)
                {
                    Logger.Debug(this.MethodName() + " | Invoice not found. UserId: {0}, InvoiceId: {1}", userId, id);
                    return null;
                }

                const string itemsSql = "SELECT * FROM InvoiceItems WHERE InvoiceId = @InvoiceId;";
                var items = await connection.QueryAsync<InvoiceItem>(itemsSql, new { InvoiceId = id });
                invoice.Items = items.ToList();

                const string storeSql = "SELECT * FROM StoreProfiles WHERE UserId = @UserId LIMIT 1;";
                var store = await connection.QuerySingleOrDefaultAsync<StoreProfile>(storeSql, new { UserId = invoice.UserId });
                invoice.Store = store;

                Logger.Debug(this.MethodName() + " | Invoice loaded. UserId: {0}, InvoiceId: {1}, ItemCount: {2}", userId, id, invoice.Items.Count);
                return invoice;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
                throw;
            }
        }

        public async Task<InvoiceSummaryDto> GetInvoiceSummaryAsync(int userId, DateTime? fromDate = null, DateTime? toDate = null)
        {
            try
            {
                using var connection = await _dbConnectionFactory.CreateConnectionAsync();

                var whereClauses = new List<string> { "UserId = @UserId" };
                var parameters = new DynamicParameters();
                parameters.Add("UserId", userId);

                if (fromDate.HasValue)
                {
                    whereClauses.Add("CreatedAt >= @FromDate");
                    parameters.Add("FromDate", fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    whereClauses.Add("CreatedAt <= @ToDate");
                    parameters.Add("ToDate", toDate.Value);
                }

                var whereSql = string.Join(" AND ", whereClauses);
                string summarySql = $@"
                SELECT 
                    COUNT(*) AS TotalTransactions,
                    COALESCE(SUM(GrandTotal), 0) AS TotalSale,
                    COALESCE(SUM(BalanceDue), 0) AS BalanceDue
                FROM Invoices
                WHERE {whereSql};";

                var summary = await connection.QuerySingleOrDefaultAsync<InvoiceSummaryDto>(summarySql, parameters);
                Logger.Debug(this.MethodName() + " | Invoice summary found: {0}. UserId: {1}", summary is not null, userId);
                return summary ?? new InvoiceSummaryDto();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
                throw;
            }
        }

        public async Task<bool> UpdateInvoiceAsync(int id, int userId, Dtos.UpdateInvoiceDto dto)
        {
            try
            {
                using var connection = await _dbConnectionFactory.CreateConnectionAsync();

                const string getInvoiceSql = "SELECT * FROM Invoices WHERE Id = @Id AND UserId = @UserId LIMIT 1;";
                var invoice = await connection.QuerySingleOrDefaultAsync<Invoice>(getInvoiceSql, new { Id = id, UserId = userId });
                if (invoice == null) return false;

                bool isReceived = dto.IsReceived;
                decimal amountReceived = isReceived && dto.AmountReceived == 0 ? invoice.GrandTotal : dto.AmountReceived;
                decimal balanceDue = Math.Max(0m, invoice.GrandTotal - amountReceived);

                const string updateSql = @"
                UPDATE Invoices 
                SET CustomerName = @CustomerName,
                    CustomerMobileNumber = @CustomerMobileNumber,
                    IsReceived = @IsReceived,
                    AmountReceived = @AmountReceived,
                    BalanceDue = @BalanceDue
                WHERE Id = @Id AND UserId = @UserId;";

                var rows = await connection.ExecuteAsync(updateSql, new
                {
                    Id = id,
                    UserId = userId,
                    CustomerName = dto.CustomerName.Trim(),
                    CustomerMobileNumber = dto.CustomerMobileNumber.Trim(),
                    IsReceived = isReceived,
                    AmountReceived = amountReceived,
                    BalanceDue = balanceDue
                });

                if (rows > 0)
                    Logger.Info(this.MethodName() + " | Invoice updated. UserId: {0}, InvoiceId: {1}", userId, id);
                return rows > 0;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
                throw;
            }
        }

        public async Task<bool> DeleteInvoiceAsync(int id, int userId)
        {
            try
            {
                using var connection = await _dbConnectionFactory.CreateConnectionAsync();

                const string deleteSql = "DELETE FROM Invoices WHERE Id = @Id AND UserId = @UserId;";
                var rows = await connection.ExecuteAsync(deleteSql, new { Id = id, UserId = userId });
                if (rows > 0)
                    Logger.Info(this.MethodName() + " | Invoice deleted. UserId: {0}, InvoiceId: {1}", userId, id);
                return rows > 0;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
                throw;
            }
        }
    }
}
