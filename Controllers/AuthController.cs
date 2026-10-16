using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using StoreManagement.Api.Dtos;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;
using StoreManagement.Api.Repositories;
using StoreManagement.Api.Services;

namespace StoreManagement.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IJwtService _jwtService;
    private readonly IPasswordResetRepository _passwordResetRepository;
    private readonly IEmailQueue _emailQueue;
    private readonly IGeoLocationService _geoLocationService;
    private readonly IConfiguration _configuration;
    private static readonly Logger Logger = LogManager.GetLogger("AuthController");

    public AuthController(IUserRepository userRepository, IRefreshTokenRepository refreshTokenRepository,
        IJwtService jwtService, IPasswordResetRepository passwordResetRepository,
        IEmailQueue emailQueue, IGeoLocationService geoLocationService, IConfiguration configuration)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _jwtService = jwtService;
        _passwordResetRepository = passwordResetRepository;
        _emailQueue = emailQueue;
        _geoLocationService = geoLocationService;
        _configuration = configuration;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ApiResponse.ErrorResult("Invalid registration data."));
        if (await _userRepository.GetByUsernameAsync(dto.Username) != null)
            return BadRequest(ApiResponse.ErrorResult("Username is already taken."));

        var user = new User { Username = dto.Username, PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password) };
        user.Id = await _userRepository.CreateUserAsync(user);
        var auth = await IssueTokensAsync(user, dto.DeviceId, dto.DeviceName);
        return auth is null
            ? Conflict(ApiResponse.ErrorResult("This account is already active on two devices. Sign out from another device to continue."))
            : Ok(ApiResponse<AuthResponseDto>.SuccessResult(auth, "User registered successfully."));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ApiResponse.ErrorResult("Invalid login credentials format."));
        var user = await _userRepository.GetByUsernameAsync(dto.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Unauthorized(ApiResponse.ErrorResult("Invalid username or password."));

        var auth = await IssueTokensAsync(user, dto.DeviceId, dto.DeviceName);
        return auth is null
            ? Conflict(ApiResponse.ErrorResult("This account is already active on two devices. Sign out from another device to continue."))
            : Ok(ApiResponse<AuthResponseDto>.SuccessResult(auth, "Login successful."));
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ApiResponse.ErrorResult("A refresh token is required."));

        var tokenHash = HashToken(dto.RefreshToken);
        var existing = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash);
        if (existing == null || existing.RevokedAt != null || existing.ExpiresAt <= DateTime.UtcNow)
            return Unauthorized(ApiResponse.ErrorResult("Session expired. Please log in again."));

        var user = await _userRepository.GetByIdAsync(existing.UserId);
        if (user == null) return Unauthorized(ApiResponse.ErrorResult("Session expired. Please log in again."));

        var rawReplacement = GenerateRefreshToken();
        var replacement = CreateRefreshToken(user.Id, rawReplacement, existing.DeviceId, existing.DeviceName);
        if (!await _refreshTokenRepository.RotateAsync(tokenHash, replacement))
        {
            // A refresh token is single-use. A concurrent/replayed use is rejected.
            return Unauthorized(ApiResponse.ErrorResult("Session expired. Please log in again."));
        }

        return Ok(ApiResponse<AuthResponseDto>.SuccessResult(
            CreateAuthResponse(user, _jwtService.GenerateToken(user.Id, user.Username), rawReplacement),
            "Session refreshed."));
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequestDto dto)
    {
        if (ModelState.IsValid) await _refreshTokenRepository.RevokeAsync(HashToken(dto.RefreshToken));
        return Ok(ApiResponse.SuccessResult("Logged out."));
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto, CancellationToken cancellationToken)
    {
        // This response deliberately does not reveal whether a store email exists,
        // whether it is rate-limited, or whether mail delivery failed.
        const string genericMessage = "If that email is registered, a password reset link will be sent shortly.";
        if (!ModelState.IsValid) return Ok(ApiResponse.SuccessResult(genericMessage));

        var publicIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        // Do this best-effort work before the account lookup too, so an existing
        // address does not have a distinctive response time.
        var (city, region) = await _geoLocationService.LookupAsync(publicIp, cancellationToken);
        var account = await _passwordResetRepository.FindUserByStoreEmailAsync(dto.Email.Trim().ToLowerInvariant());
        if (!account.HasValue) return Ok(ApiResponse.SuccessResult(genericMessage));

        var rawToken = GenerateResetToken();
        var request = new PasswordResetRequest
        {
            UserId = account.Value.UserId,
            TokenHash = HashToken(rawToken),
            RequestedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            DeviceType = Clean(dto.DeviceType, 60),
            OperatingSystem = Clean(dto.OperatingSystem, 100),
            Browser = Clean(dto.Browser ?? Request.Headers.UserAgent.ToString(), 200),
            DeviceId = Clean(dto.DeviceId, 128),
            PublicIp = Clean(publicIp, 45), City = Clean(city, 100), Region = Clean(region, 100),
            GpsLatitude = dto.GpsLatitude, GpsLongitude = dto.GpsLongitude
        };
        if (await _passwordResetRepository.TryCreateAsync(request) != PasswordResetCreateResult.Created)
            return Ok(ApiResponse.SuccessResult(genericMessage));

        try
        {
            var resetUrl = BuildResetUrl(rawToken);
            _emailQueue.Queue((email, ct) => email.SendPasswordResetAsync(account.Value.Email, resetUrl, request, ct));
        }
        catch (Exception ex) { Logger.Error(ex, "Password reset email could not be queued for user {0}.", account.Value.UserId); }
        return Ok(ApiResponse.SuccessResult(genericMessage));
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(dto.NewPassword))
            return BadRequest(ApiResponse.ErrorResult("Invalid password reset request."));
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        var result = await _passwordResetRepository.UseTokenAndResetPasswordAsync(HashToken(dto.Token), passwordHash);
        if (!result.Success) return BadRequest(ApiResponse.ErrorResult("This password reset link is invalid, expired, or has already been used."));
        _emailQueue.Queue((email, ct) => email.SendPasswordChangedConfirmationAsync(result.Email, ct));
        return Ok(ApiResponse.SuccessResult("Password changed. Please sign in with your new password."));
    }

    private async Task<AuthResponseDto?> IssueTokensAsync(User user, string deviceId, string? deviceName)
    {
        var rawRefreshToken = GenerateRefreshToken();
        var refreshToken = CreateRefreshToken(user.Id, rawRefreshToken, deviceId, deviceName);
        if (!await _refreshTokenRepository.TryCreateForDeviceAsync(refreshToken, maximumDevices: 2))
            return null;
        return CreateAuthResponse(user, _jwtService.GenerateToken(user.Id, user.Username), rawRefreshToken);
    }

    private AuthResponseDto CreateAuthResponse(User user, string accessToken, string refreshToken) => new()
    {
        UserId = user.Id,
        Username = user.Username,
        Token = accessToken,
        RefreshToken = refreshToken
    };

    private RefreshToken CreateRefreshToken(int userId, string rawToken, string deviceId, string? deviceName)
    {
        var lifetimeDays = int.TryParse(_configuration["JwtSettings:RefreshTokenExpiryInDays"], out var days) ? days : 30;
        return new RefreshToken
        {
            UserId = userId,
            DeviceId = deviceId.Trim(),
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName.Trim(),
            TokenHash = HashToken(rawToken),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(lifetimeDays)
        };
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd((char)61);
    }

    private string BuildResetUrl(string rawToken)
    {
        var baseUrl = _configuration["Email:PasswordResetUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Email:PasswordResetUrl must be configured before password reset can be used.");
        // The fragment is not transmitted to the web host, reducing accidental
        // token exposure in web-server logs and Referer headers.
        return $"{baseUrl.TrimEnd('#')}#token={Uri.EscapeDataString(rawToken)}";
    }

    private static string GenerateResetToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
        .Replace("+", "-").Replace("/", "_").TrimEnd('=');
    private static string? Clean(string? value, int maxLength) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
