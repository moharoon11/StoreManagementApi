namespace StoreManagement.Api.Services
{
    public interface ICloudinaryService
    {
        Task<string?> UploadImageAsync(IFormFile file, string folder = "store_management");
        Task<bool> DeleteImageAsync(string publicId);
    }
}
