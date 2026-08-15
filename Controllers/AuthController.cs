using Microsoft.AspNetCore.Mvc;
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

        public AuthController(IUserRepository userRepository, IJwtService jwtService)
        {
            _userRepository = userRepository;
            _jwtService = jwtService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse.ErrorResult("Invalid registration data."));
            }

            var existingUser = await _userRepository.GetByUsernameAsync(dto.Username);
            if (existingUser != null)
            {
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

            return Ok(ApiResponse<AuthResponseDto>.SuccessResult(response, "User registered successfully."));
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse.ErrorResult("Invalid login credentials format."));
            }

            var user = await _userRepository.GetByUsernameAsync(dto.Username);
            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                return Unauthorized(ApiResponse.ErrorResult("Invalid username or password."));
            }

            var token = _jwtService.GenerateToken(user.Id, user.Username);

            var response = new AuthResponseDto
            {
                UserId = user.Id,
                Username = user.Username,
                Token = token
            };

            return Ok(ApiResponse<AuthResponseDto>.SuccessResult(response, "Login successful."));
        }
    }
}
