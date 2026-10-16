using Dapper;
using StoreManagement.Api.Data;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories;

public class PasswordResetRepository : IPasswordResetRepository
{
    private readonly IDbConnectionFactory _db;
    public PasswordResetRepository(IDbConnectionFactory db) => _db = db;

    public async Task<(int UserId, string Email)?> FindUserByStoreEmailAsync(string normalizedEmail)
    {
        await using var connection = await _db.CreateConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<(int UserId, string Email)>(@"
            SELECT u.Id AS UserId, sp.Email
            FROM Users u INNER JOIN StoreProfiles sp ON sp.UserId = u.Id
            WHERE LOWER(TRIM(sp.Email)) = @normalizedEmail LIMIT 1;", new { normalizedEmail });
    }

    public async Task<PasswordResetCreateResult> TryCreateAsync(PasswordResetRequest request)
    {
        await using var connection = await _db.CreateConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("SELECT Id FROM Users WHERE Id = @UserId FOR UPDATE;", new { request.UserId }, transaction);

        var latest = await connection.ExecuteScalarAsync<DateTime?>(@"
            SELECT MAX(RequestedAt) FROM PasswordResetRequests WHERE UserId = @UserId;",
            new { request.UserId }, transaction);
        if (latest.HasValue && latest.Value > DateTime.UtcNow.AddMinutes(-5))
        {
            await transaction.RollbackAsync();
            return PasswordResetCreateResult.Cooldown;
        }
        var recent = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM PasswordResetRequests
            WHERE UserId = @UserId AND RequestedAt > UTC_TIMESTAMP() - INTERVAL 1 HOUR;",
            new { request.UserId }, transaction);
        if (recent >= 3)
        {
            await transaction.RollbackAsync();
            return PasswordResetCreateResult.HourlyLimit;
        }
        await connection.ExecuteAsync(@"
            INSERT INTO PasswordResetRequests
            (UserId, TokenHash, RequestedAt, ExpiresAt, DeviceType, OperatingSystem, Browser, DeviceId, PublicIp, City, Region, GpsLatitude, GpsLongitude)
            VALUES (@UserId, @TokenHash, UTC_TIMESTAMP(), @ExpiresAt, @DeviceType, @OperatingSystem, @Browser, @DeviceId, @PublicIp, @City, @Region, @GpsLatitude, @GpsLongitude);", request, transaction);
        await transaction.CommitAsync();
        return PasswordResetCreateResult.Created;
    }

    public async Task<(bool Success, int UserId, string Email)> UseTokenAndResetPasswordAsync(string tokenHash, string passwordHash)
    {
        await using var connection = await _db.CreateConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var reset = await connection.QuerySingleOrDefaultAsync<PasswordResetRequest>(@"
            SELECT * FROM PasswordResetRequests WHERE TokenHash = @tokenHash FOR UPDATE;", new { tokenHash }, transaction);
        if (reset is null || reset.UsedAt is not null || reset.InvalidatedAt is not null || reset.ExpiresAt <= DateTime.UtcNow)
        {
            await transaction.RollbackAsync();
            return (false, 0, string.Empty);
        }
        var email = await connection.ExecuteScalarAsync<string?>("SELECT Email FROM StoreProfiles WHERE UserId = @UserId LIMIT 1;", new { reset.UserId }, transaction);
        if (string.IsNullOrWhiteSpace(email)) { await transaction.RollbackAsync(); return (false, 0, string.Empty); }
        await connection.ExecuteAsync("UPDATE Users SET PasswordHash = @passwordHash, UpdatedAt = UTC_TIMESTAMP() WHERE Id = @userId;", new { passwordHash, userId = reset.UserId }, transaction);
        await connection.ExecuteAsync(@"UPDATE PasswordResetRequests
            SET InvalidatedAt = UTC_TIMESTAMP() WHERE UserId = @userId AND UsedAt IS NULL AND InvalidatedAt IS NULL;", new { userId = reset.UserId }, transaction);
        await connection.ExecuteAsync(@"UPDATE PasswordResetRequests SET UsedAt = UTC_TIMESTAMP(), InvalidatedAt = NULL WHERE Id = @id;", new { id = reset.Id }, transaction);
        await connection.ExecuteAsync("UPDATE RefreshTokens SET RevokedAt = COALESCE(RevokedAt, UTC_TIMESTAMP()) WHERE UserId = @userId;", new { userId = reset.UserId }, transaction);
        await transaction.CommitAsync();
        return (true, reset.UserId, email);
    }
}
