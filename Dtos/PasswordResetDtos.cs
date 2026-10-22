using System.ComponentModel.DataAnnotations;

namespace StoreManagement.Api.Dtos;

public class ForgotPasswordDto
{
    [Required, StringLength(100, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = string.Empty;
}

public class VerifyPasswordResetOtpDto
{
    [Required, StringLength(100, MinimumLength = 3)] public string Username { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(150)] public string Email { get; set; } = string.Empty;
    [Required, RegularExpression("^[0-9]{6}$")] public string Otp { get; set; } = string.Empty;
}

public class ResetPasswordDto
{
    [Required, StringLength(512, MinimumLength = 32)] public string ResetToken { get; set; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 8)] public string NewPassword { get; set; } = string.Empty;
}

public sealed class VerifyPasswordResetOtpResponseDto
{
    public string ResetToken { get; init; } = string.Empty;
}
