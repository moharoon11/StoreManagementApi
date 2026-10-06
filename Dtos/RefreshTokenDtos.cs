using System.ComponentModel.DataAnnotations;

namespace StoreManagement.Api.Dtos;

public class RefreshTokenRequestDto
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
