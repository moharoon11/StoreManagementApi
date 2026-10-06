using Dapper;
using StoreManagement.Api.Data;
using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public RefreshTokenRepository(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    public async Task CreateAsync(RefreshToken refreshToken)
    {
        await using var connection = await _dbConnectionFactory.CreateConnectionAsync();
        await connection.ExecuteAsync(InsertSql, refreshToken);
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

    private const string InsertSql = @"
        INSERT INTO RefreshTokens (UserId, TokenHash, ExpiresAt, CreatedAt)
        VALUES (@UserId, @TokenHash, @ExpiresAt, @CreatedAt);";
}
