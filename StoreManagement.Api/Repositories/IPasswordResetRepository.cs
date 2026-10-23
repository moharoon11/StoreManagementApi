namespace StoreManagement.Api.Repositories;

public enum PasswordResetOtpIssueResult { Issued, Cooldown, HourlyLimit }

public interface IPasswordResetRepository
{
    Task<(int UserId, string Username, string Email)?> FindUserByUsernameAndStoreEmailAsync(string normalizedUsername, string normalizedEmail);
    Task<PasswordResetOtpIssueResult> IssueOtpAsync(int userId, string username, string email, string otpHmacHash);
    Task<bool> VerifyOtpAsync(string normalizedUsername, string normalizedEmail, string otpHmacHash, string resetTokenHmacHash);
    Task<(bool Success, string Email)> UseVerifiedOtpAndResetPasswordAsync(string resetTokenHmacHash, string passwordHash);
}
