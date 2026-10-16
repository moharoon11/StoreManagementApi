using StoreManagement.Api.Models;

namespace StoreManagement.Api.Services;

public interface IEmailService
{
    Task SendPasswordResetAsync(string recipient, string resetUrl, PasswordResetRequest request, CancellationToken cancellationToken = default);
    Task SendPasswordChangedConfirmationAsync(string recipient, CancellationToken cancellationToken = default);
}
