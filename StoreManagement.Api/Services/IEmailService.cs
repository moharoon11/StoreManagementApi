namespace StoreManagement.Api.Services;

public interface IEmailService
{
    Task SendPasswordResetOtpAsync(string recipient, string otp, CancellationToken cancellationToken = default);
    Task SendPasswordChangedConfirmationAsync(string recipient, CancellationToken cancellationToken = default);
}
