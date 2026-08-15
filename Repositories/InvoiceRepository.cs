using Dapper;
using StoreManagement.Api.Data;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public class InvoiceRepository : IInvoiceRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public InvoiceRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<PagedResponse<Invoice>> GetInvoiceHistoryAsync(int userId, int pageNumber = 1, int pageSize = 10, DateTime? fromDate = null, DateTime? toDate = null)
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

            return new PagedResponse<Invoice>(invoices, pageNumber, pageSize, totalCount);
        }

        public async Task<Invoice?> GetByIdAsync(int id, int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();

            const string invoiceSql = "SELECT * FROM Invoices WHERE Id = @Id AND UserId = @UserId LIMIT 1;";
            var invoice = await connection.QuerySingleOrDefaultAsync<Invoice>(invoiceSql, new { Id = id, UserId = userId });

            if (invoice == null) return null;

            const string itemsSql = "SELECT * FROM InvoiceItems WHERE InvoiceId = @InvoiceId;";
            var items = await connection.QueryAsync<InvoiceItem>(itemsSql, new { InvoiceId = id });
            invoice.Items = items.ToList();

            const string storeSql = "SELECT * FROM StoreProfiles WHERE UserId = @UserId LIMIT 1;";
            var store = await connection.QuerySingleOrDefaultAsync<StoreProfile>(storeSql, new { UserId = userId });
            invoice.Store = store;

            return invoice;
        }
    }
}
