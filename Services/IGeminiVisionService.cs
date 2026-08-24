namespace StoreManagement.Api.Services
{
    public interface IGeminiVisionService
    {
        Task<string?> ExtractBillDataAsync(IFormFile imageFile);
    }
}
