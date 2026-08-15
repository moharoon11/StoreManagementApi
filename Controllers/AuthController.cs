using Microsoft.AspNetCore.Mvc;
using NLog;
using StoreManagement.Api.Dtos;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;
using StoreManagement.Api.Repositories;
using StoreManagement.Api.Services;

namespace StoreManagement.Api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IJwtService _jwtService;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public AuthController(IUserRepository userRepository, IJwtService jwtService)
        {
            _userRepository = userRepository;
            _jwtService = jwtService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            Logger.Debug("Register started. Username: {0}", dto.Username);
            if (!ModelState.IsValid)
            {
                Logger.Error("Register failed. Reason: Invalid registration data. Username: {0}", dto.Username);
                return BadRequest(ApiResponse.ErrorResult("Invalid registration data."));
            }

            var existingUser = await _userRepository.GetByUsernameAsync(dto.Username);
            if (existingUser != null)
            {
                Logger.Error("Register failed. Reason: Username is already taken. Username: {0}", dto.Username);
                return BadRequest(ApiResponse.ErrorResult("Username is already taken."));
            }

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            var user = new User
            {
                Username = dto.Username,
                PasswordHash = passwordHash
            };

            var userId = await _userRepository.CreateUserAsync(user);
            var token = _jwtService.GenerateToken(userId, dto.Username);

            var response = new AuthResponseDto
            {
                UserId = userId,
                Username = dto.Username,
                Token = token
            };

            Logger.Info("Register succeeded. UserId: {0}, Username: {1}", userId, dto.Username);
            return Ok(ApiResponse<AuthResponseDto>.SuccessResult(response, "User registered successfully."));
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            Logger.Debug("Login started. Username: {0}", dto.Username);
            if (!ModelState.IsValid)
            {
                Logger.Error("Login failed. Reason: Invalid login credentials format. Username: {0}", dto.Username);
                return BadRequest(ApiResponse.ErrorResult("Invalid login credentials format."));
            }

            var user = await _userRepository.GetByUsernameAsync(dto.Username);
            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                Logger.Error("Login failed. Reason: Invalid username or password. Username: {0}", dto.Username);
                return Unauthorized(ApiResponse.ErrorResult("Invalid username or password."));
            }

            var token = _jwtService.GenerateToken(user.Id, user.Username);

            var response = new AuthResponseDto
            {
                UserId = user.Id,
                Username = user.Username,
                Token = token
            };

            Logger.Info("Login succeeded. UserId: {0}, Username: {1}", user.Id, user.Username);
            return Ok(ApiResponse<AuthResponseDto>.SuccessResult(response, "Login successful."));
        }
    }
}
