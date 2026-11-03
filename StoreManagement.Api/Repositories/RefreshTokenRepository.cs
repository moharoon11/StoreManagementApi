using Dapper;
using NLog;
using StoreManagement.Api.Data;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public RefreshTokenRepository(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    public async Task<bool> TryCreateForDeviceAsync(RefreshToken refreshToken, int maximumDevices)
    {
        try
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
                Logger.Warn(this.MethodName() + " | Session creation blocked by device limit. UserId: {0}", refreshToken.UserId);
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
            Logger.Debug(this.MethodName() + " | Session created. UserId: {0}", refreshToken.UserId);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash)
    {
        try
        {
            await using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            var token = await connection.QuerySingleOrDefaultAsync<RefreshToken>(
                "SELECT * FROM RefreshTokens WHERE TokenHash = @tokenHash LIMIT 1;", new { tokenHash });
            Logger.Debug(this.MethodName() + " | Refresh token found: {0}", token is not null);
            return token;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<bool> RotateAsync(string currentTokenHash, RefreshToken replacement)
    {
        try
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
                Logger.Warn(this.MethodName() + " | Session rotation rejected: token already used or expired. UserId: {0}", replacement.UserId);
                return false;
            }

            await connection.ExecuteAsync(InsertSql, replacement, transaction);
            await transaction.CommitAsync();
            Logger.Debug(this.MethodName() + " | Session rotated. UserId: {0}", replacement.UserId);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task RevokeAsync(string tokenHash)
    {
        try
        {
            await using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            var affected = await connection.ExecuteAsync(@"
            UPDATE RefreshTokens SET RevokedAt = COALESCE(RevokedAt, UTC_TIMESTAMP())
            WHERE TokenHash = @tokenHash;", new { tokenHash });
            Logger.Debug(this.MethodName() + " | Session revoke processed. Rows: {0}", affected);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task RevokeAllForUserAsync(int userId)
    {
        try
        {
            await using var connection = await _dbConnectionFactory.CreateConnectionAsync();
            var affected = await connection.ExecuteAsync(@"
            UPDATE RefreshTokens SET RevokedAt = COALESCE(RevokedAt, UTC_TIMESTAMP())
            WHERE UserId = @userId;", new { userId });
            Logger.Info(this.MethodName() + " | User sessions revoked. UserId: {0}, Rows: {1}", userId, affected);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    private const string InsertSql = @"
        INSERT INTO RefreshTokens (UserId, DeviceId, DeviceName, TokenHash, ExpiresAt, CreatedAt)
        VALUES (@UserId, @DeviceId, @DeviceName, @TokenHash, @ExpiresAt, @CreatedAt);";
}
