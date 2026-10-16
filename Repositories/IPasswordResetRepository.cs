using StoreManagement.Api.Models;

namespace StoreManagement.Api.Repositories;

public enum PasswordResetCreateResult { Created, Cooldown, HourlyLimit }

public interface IPasswordResetRepository
{
    Task<(int UserId, string Email)?> FindUserByStoreEmailAsync(string normalizedEmail);
    Task<PasswordResetCreateResult> TryCreateAsync(PasswordResetRequest request);
    Task<(bool Success, int UserId, string Email)> UseTokenAndResetPasswordAsync(string tokenHash, string passwordHash);
}
