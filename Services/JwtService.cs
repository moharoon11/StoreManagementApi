using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NLog;

namespace StoreManagement.Api.Services
{
    public class JwtService : IJwtService
    {
        private readonly IConfiguration _configuration;
        private static readonly Logger Logger = LogManager.GetLogger("JwtService");

        public JwtService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GenerateToken(int userId, string username)
        {
            Logger.Debug("GenerateToken started. UserId: {0}, Username: {1}", userId, username);
            var secretKey = _configuration["JwtSettings:SecretKey"] ?? "DefaultSecretKeyThatIsAtLeast32BytesLongForSecurity!";
            var issuer = _configuration["JwtSettings:Issuer"] ?? "StoreManagementApi";
            var audience = _configuration["JwtSettings:Audience"] ?? "StoreManagementFlutterApp";
            var expiryDays = int.TryParse(_configuration["JwtSettings:ExpiryInDays"], out var days) ? days : 7;

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim("userId", userId.ToString())
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddDays(expiryDays),
                signingCredentials: credentials);

            var generatedToken = new JwtSecurityTokenHandler().WriteToken(token);
            Logger.Info("GenerateToken succeeded. UserId: {0}, Username: {1}", userId, username);
            return generatedToken;
        }
    }
}
