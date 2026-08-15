using Dapper;
using StoreManagement.Api.Data;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public class StoreProfileRepository : IStoreProfileRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public StoreProfileRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<StoreProfile?> GetByUserIdAsync(int userId)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = "SELECT * FROM StoreProfiles WHERE UserId = @UserId LIMIT 1;";
            return await connection.QuerySingleOrDefaultAsync<StoreProfile>(sql, new { UserId = userId });
        }

        public async Task<int> CreateOrUpdateProfileAsync(StoreProfile profile)
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            const string sql = @"
                INSERT INTO StoreProfiles 
                    (UserId, StoreName, OwnerName, LogoUrl, Address, City, District, Pincode, Email, GstNumber, Phone, AlternatePhone, AboutUs, CreatedAt, UpdatedAt)
                VALUES 
                    (@UserId, @StoreName, @OwnerName, @LogoUrl, @Address, @City, @District, @Pincode, @Email, @GstNumber, @Phone, @AlternatePhone, @AboutUs, NOW(), NOW())
                ON DUPLICATE KEY UPDATE
                    StoreName = VALUES(StoreName),
                    OwnerName = VALUES(OwnerName),
                    LogoUrl = COALESCE(VALUES(LogoUrl), LogoUrl),
                    Address = VALUES(Address),
                    City = VALUES(City),
                    District = VALUES(District),
                    Pincode = VALUES(Pincode),
                    Email = VALUES(Email),
                    GstNumber = VALUES(GstNumber),
                    Phone = VALUES(Phone),
                    AlternatePhone = VALUES(AlternatePhone),
                    AboutUs = VALUES(AboutUs),
                    UpdatedAt = NOW();
                SELECT Id FROM StoreProfiles WHERE UserId = @UserId LIMIT 1;";
            
            return await connection.ExecuteScalarAsync<int>(sql, profile);
        }
    }
}
