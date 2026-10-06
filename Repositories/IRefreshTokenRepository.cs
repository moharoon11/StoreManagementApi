using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories;

public interface IRefreshTokenRepository
{
    Task CreateAsync(RefreshToken refreshToken);
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);
    Task<bool> RotateAsync(string currentTokenHash, RefreshToken replacement);
    Task RevokeAsync(string tokenHash);
}
