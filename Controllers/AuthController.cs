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
    private readonly IConfiguration _configuration;
    private static readonly Logger Logger = LogManager.GetLogger("AuthController");

    public AuthController(IUserRepository userRepository, IRefreshTokenRepository refreshTokenRepository,
        IJwtService jwtService, IConfiguration configuration)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _jwtService = jwtService;
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

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
