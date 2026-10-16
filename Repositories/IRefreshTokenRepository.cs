using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories;

public interface IRefreshTokenRepository
{
    Task<bool> TryCreateForDeviceAsync(RefreshToken refreshToken, int maximumDevices);
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);
    Task<bool> RotateAsync(string currentTokenHash, RefreshToken replacement);
    Task RevokeAsync(string tokenHash);
    Task RevokeAllForUserAsync(int userId);
}
