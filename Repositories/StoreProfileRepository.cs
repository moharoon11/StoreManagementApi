using Dapper;
using NLog;
using StoreManagement.Api.Data;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories
{
    public class StoreProfileRepository : IStoreProfileRepository
    {
        private static readonly Logger Logger = LogManager.GetLogger("StoreProfileRepository");
        private const string ClassName = nameof(StoreProfileRepository);
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public StoreProfileRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<StoreProfile?> GetByUserIdAsync(int userId)
        {
            Logger.Debug($"{ClassName}.{nameof(GetByUserIdAsync)}() started. UserId: {userId}");
            try
            {
                using var connection = await _dbConnectionFactory.CreateConnectionAsync();
                const string sql = "SELECT * FROM StoreProfiles WHERE UserId = @UserId LIMIT 1;";
                var profile = await connection.QuerySingleOrDefaultAsync<StoreProfile>(sql, new { UserId = userId });
                Logger.Info($"{ClassName}.{nameof(GetByUserIdAsync)}() succeeded. UserId: {userId}, ProfileFound: {profile is not null}");
                return profile;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"{ClassName}.{nameof(GetByUserIdAsync)}() failed. UserId: {userId}");
                throw;
            }
        }

        public async Task<int> CreateOrUpdateProfileAsync(StoreProfile profile)
        {
            Logger.Debug($"{ClassName}.{nameof(CreateOrUpdateProfileAsync)}() started. UserId: {profile.UserId}");
            try
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
            
            var profileId = await connection.ExecuteScalarAsync<int>(sql, profile);
            Logger.Info($"{ClassName}.{nameof(CreateOrUpdateProfileAsync)}() succeeded. UserId: {profile.UserId}, ProfileId: {profileId}");
            return profileId;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"{ClassName}.{nameof(CreateOrUpdateProfileAsync)}() failed. UserId: {profile.UserId}");
                throw;
            }
        }
    }
}
