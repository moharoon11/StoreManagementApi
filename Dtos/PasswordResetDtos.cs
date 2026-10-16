using System.ComponentModel.DataAnnotations;

namespace StoreManagement.Api.Dtos;

public class ForgotPasswordDto
{
    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = string.Empty;
    [StringLength(60)] public string? DeviceType { get; set; }
    [StringLength(100)] public string? OperatingSystem { get; set; }
    [StringLength(200)] public string? Browser { get; set; }
    [StringLength(128)] public string? DeviceId { get; set; }
    // GPS is sent only after an explicit client opt-in. It is never required.
    [Range(-90, 90)] public decimal? GpsLatitude { get; set; }
    [Range(-180, 180)] public decimal? GpsLongitude { get; set; }
}

public class ResetPasswordDto
{
    [Required, StringLength(512, MinimumLength = 32)] public string Token { get; set; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 8)] public string NewPassword { get; set; } = string.Empty;
}
