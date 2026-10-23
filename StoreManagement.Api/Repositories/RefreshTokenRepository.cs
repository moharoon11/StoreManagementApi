using Dapper;
using StoreManagement.Api.Data;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public RefreshTokenRepository(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    public async Task<bool> TryCreateForDeviceAsync(RefreshToken refreshToken, int maximumDevices)
    {
        await using var connection = await _dbConnectionFactory.CreateConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // Locking the user row serializes concurrent login attempts, so two
        // near-simultaneous third-device logins cannot bypass the limit.
        await connection.ExecuteAsync(
            "SELECT Id FROM Users WHERE Id = @UserId FOR UPDATE;",
            new { refreshToken.UserId }, transaction);
        await connection.ExecuteAsync(@"
            UPDATE RefreshTokens SET RevokedAt = UTC_TIMESTAMP()
            WHERE UserId = @UserId AND RevokedAt IS NULL AND ExpiresAt <= UTC_TIMESTAMP();",
            new { refreshToken.UserId }, transaction);

        var otherDeviceCount = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(DISTINCT DeviceId) FROM RefreshTokens
            WHERE UserId = @UserId
              AND RevokedAt IS NULL
              AND ExpiresAt > UTC_TIMESTAMP()
              AND DeviceId <> @DeviceId;",
            new { refreshToken.UserId, refreshToken.DeviceId }, transaction);
        if (otherDeviceCount >= maximumDevices)
        {
            await transaction.RollbackAsync();
            return false;
        }

        // A new login on the same physical device replaces its old session,
        // rather than consuming another slot.
        await connection.ExecuteAsync(@"
            UPDATE RefreshTokens SET RevokedAt = UTC_TIMESTAMP()
            WHERE UserId = @UserId AND DeviceId = @DeviceId AND RevokedAt IS NULL;",
            new { refreshToken.UserId, refreshToken.DeviceId }, transaction);
        await connection.ExecuteAsync(InsertSql, refreshToken, transaction);
        await transaction.CommitAsync();
        return true;
    }

    public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash)
    {
        await using var connection = await _dbConnectionFactory.CreateConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<RefreshToken>(
            "SELECT * FROM RefreshTokens WHERE TokenHash = @tokenHash LIMIT 1;", new { tokenHash });
    }

    public async Task<bool> RotateAsync(string currentTokenHash, RefreshToken replacement)
    {
        await using var connection = await _dbConnectionFactory.CreateConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var revoked = await connection.ExecuteAsync(@"
            UPDATE RefreshTokens
            SET RevokedAt = UTC_TIMESTAMP(), ReplacedByTokenHash = @replacementHash
            WHERE TokenHash = @currentTokenHash
              AND RevokedAt IS NULL
              AND ExpiresAt > UTC_TIMESTAMP();",
            new { currentTokenHash, replacementHash = replacement.TokenHash }, transaction);
        if (revoked != 1)
        {
            await transaction.RollbackAsync();
            return false;
        }

        await connection.ExecuteAsync(InsertSql, replacement, transaction);
        await transaction.CommitAsync();
        return true;
    }

    public async Task RevokeAsync(string tokenHash)
    {
        await using var connection = await _dbConnectionFactory.CreateConnectionAsync();
        await connection.ExecuteAsync(@"
            UPDATE RefreshTokens SET RevokedAt = COALESCE(RevokedAt, UTC_TIMESTAMP())
            WHERE TokenHash = @tokenHash;", new { tokenHash });
    }

    public async Task RevokeAllForUserAsync(int userId)
    {
        await using var connection = await _dbConnectionFactory.CreateConnectionAsync();
        await connection.ExecuteAsync(@"
            UPDATE RefreshTokens SET RevokedAt = COALESCE(RevokedAt, UTC_TIMESTAMP())
            WHERE UserId = @userId;", new { userId });
    }

    private const string InsertSql = @"
        INSERT INTO RefreshTokens (UserId, DeviceId, DeviceName, TokenHash, ExpiresAt, CreatedAt)
        VALUES (@UserId, @DeviceId, @DeviceName, @TokenHash, @ExpiresAt, @CreatedAt);";
}
