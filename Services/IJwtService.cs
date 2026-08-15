namespace StoreManagement.Api.Services
{
    public interface IJwtService
    {
        string GenerateToken(int userId, string username);
    }
}
