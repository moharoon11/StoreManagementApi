using System.Security.Cryptography;
using Dapper;
using StoreManagement.Api.Data;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories;

public sealed class PasswordResetRepository : IPasswordResetRepository
{
    private readonly IDbConnectionFactory _db;
    public PasswordResetRepository(IDbConnectionFactory db) => _db = db;

    public async Task<(int UserId, string Username, string Email)?> FindUserByUsernameAndStoreEmailAsync(string normalizedUsername, string normalizedEmail)
    {
        await using var connection = await _db.CreateConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<(int UserId, string Username, string Email)>(@"
            SELECT u.Id AS UserId, u.Username, sp.Email
            FROM Users u INNER JOIN StoreProfiles sp ON sp.UserId = u.Id
            WHERE LOWER(TRIM(u.Username)) = @normalizedUsername
              AND LOWER(TRIM(sp.Email)) = @normalizedEmail
            LIMIT 1;", new { normalizedUsername, normalizedEmail });
    }

    public async Task<PasswordResetOtpIssueResult> IssueOtpAsync(int userId, string username, string email, string otpHmacHash)
    {
        await using var connection = await _db.CreateConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("SELECT Id FROM Users WHERE Id = @userId FOR UPDATE;", new { userId }, transaction);

        var lastIssuedAt = await connection.ExecuteScalarAsync<DateTime?>(
            "SELECT MAX(CreatedAt) FROM PasswordResetOtps WHERE UserId = @userId;", new { userId }, transaction);
        if (lastIssuedAt > DateTime.UtcNow.AddMinutes(-5))
        {
            await transaction.RollbackAsync();
            return PasswordResetOtpIssueResult.Cooldown;
        }
        var issuedInLastHour = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM PasswordResetOtps
            WHERE UserId = @userId AND CreatedAt > UTC_TIMESTAMP() - INTERVAL 1 HOUR;", new { userId }, transaction);
        if (issuedInLastHour >= 3)
        {
            await transaction.RollbackAsync();
            return PasswordResetOtpIssueResult.HourlyLimit;
        }

        await connection.ExecuteAsync(@"
            UPDATE PasswordResetOtps SET InvalidatedAt = UTC_TIMESTAMP()
            WHERE UserId = @userId AND UsedAt IS NULL AND InvalidatedAt IS NULL;", new { userId }, transaction);
        await connection.ExecuteAsync(@"
            INSERT INTO PasswordResetOtps
                (UserId, Username, Email, OtpHmacHash, CreatedAt, ExpiresAt, FailedAttempts)
            VALUES (@userId, @username, @email, @otpHmacHash, UTC_TIMESTAMP(), DATE_ADD(UTC_TIMESTAMP(), INTERVAL 5 MINUTE), 0);",
            new { userId, username, email, otpHmacHash }, transaction);
        await transaction.CommitAsync();
        return PasswordResetOtpIssueResult.Issued;
    }

    public async Task<bool> VerifyOtpAsync(string normalizedUsername, string normalizedEmail, string otpHmacHash, string resetTokenHmacHash)
    {
        await using var connection = await _db.CreateConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var otp = await connection.QuerySingleOrDefaultAsync<PasswordResetOtp>(@"
            SELECT o.* FROM PasswordResetOtps o
            INNER JOIN Users u ON u.Id = o.UserId
            INNER JOIN StoreProfiles sp ON sp.UserId = o.UserId
            WHERE LOWER(TRIM(u.Username)) = @normalizedUsername
              AND LOWER(TRIM(sp.Email)) = @normalizedEmail
              AND o.InvalidatedAt IS NULL AND o.UsedAt IS NULL AND o.VerifiedAt IS NULL
            ORDER BY o.Id DESC LIMIT 1 FOR UPDATE;", new { normalizedUsername, normalizedEmail }, transaction);
        if (otp is null) { await transaction.RollbackAsync(); return false; }
        if (otp.ExpiresAt <= DateTime.UtcNow || otp.FailedAttempts >= 3)
        {
            await connection.ExecuteAsync("UPDATE PasswordResetOtps SET InvalidatedAt = UTC_TIMESTAMP() WHERE Id = @id;", new { otp.Id }, transaction);
            await transaction.CommitAsync();
            return false;
        }

        var supplied = Convert.FromHexString(otpHmacHash);
        var expected = Convert.FromHexString(otp.OtpHmacHash);
        if (!CryptographicOperations.FixedTimeEquals(supplied, expected))
        {
            var attempts = otp.FailedAttempts + 1;
            await connection.ExecuteAsync(@"
                UPDATE PasswordResetOtps
                SET FailedAttempts = @attempts,
                    InvalidatedAt = CASE WHEN @attempts >= 3 THEN UTC_TIMESTAMP() ELSE InvalidatedAt END
                WHERE Id = @id;", new { attempts, otp.Id }, transaction);
            await transaction.CommitAsync();
            return false;
        }

        await connection.ExecuteAsync(@"
            UPDATE PasswordResetOtps
            SET VerifiedAt = UTC_TIMESTAMP(), ResetTokenHmacHash = @resetTokenHmacHash
            WHERE Id = @id;", new { resetTokenHmacHash, otp.Id }, transaction);
        await transaction.CommitAsync();
        return true;
    }

    public async Task<(bool Success, string Email)> UseVerifiedOtpAndResetPasswordAsync(string resetTokenHmacHash, string passwordHash)
    {
        await using var connection = await _db.CreateConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var otp = await connection.QuerySingleOrDefaultAsync<PasswordResetOtp>(
            "SELECT * FROM PasswordResetOtps WHERE ResetTokenHmacHash = @resetTokenHmacHash FOR UPDATE;", new { resetTokenHmacHash }, transaction);
        if (otp is null || otp.VerifiedAt is null || otp.UsedAt is not null || otp.InvalidatedAt is not null || otp.ExpiresAt <= DateTime.UtcNow)
        {
            await transaction.RollbackAsync();
            return (false, string.Empty);
        }

        await connection.ExecuteAsync("UPDATE Users SET PasswordHash = @passwordHash, UpdatedAt = UTC_TIMESTAMP() WHERE Id = @userId;", new { passwordHash, userId = otp.UserId }, transaction);
        await connection.ExecuteAsync(@"
            UPDATE PasswordResetOtps SET InvalidatedAt = UTC_TIMESTAMP()
            WHERE UserId = @userId AND Id <> @id AND UsedAt IS NULL AND InvalidatedAt IS NULL;", new { userId = otp.UserId, otp.Id }, transaction);
        await connection.ExecuteAsync("UPDATE PasswordResetOtps SET UsedAt = UTC_TIMESTAMP() WHERE Id = @id;", new { otp.Id }, transaction);
        await connection.ExecuteAsync("UPDATE RefreshTokens SET RevokedAt = COALESCE(RevokedAt, UTC_TIMESTAMP()) WHERE UserId = @userId;", new { userId = otp.UserId }, transaction);
        await transaction.CommitAsync();
        return (true, otp.Email);
    }
}
