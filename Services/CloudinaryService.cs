using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using NLog;

namespace StoreManagement.Api.Services
{
    public class CloudinaryService : ICloudinaryService
    {
        private readonly Cloudinary _cloudinary;
        private static readonly Logger Logger = LogManager.GetLogger("CloudinaryService");

        public CloudinaryService(IConfiguration configuration)
        {
            var cloudName = configuration["Cloudinary:CloudName"];
            var apiKey = configuration["Cloudinary:ApiKey"];
            var apiSecret = configuration["Cloudinary:ApiSecret"];

            if (!string.IsNullOrEmpty(cloudName) && !string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret))
            {
                var account = new Account(cloudName, apiKey, apiSecret);
                _cloudinary = new Cloudinary(account);
            }
            else
            {
                _cloudinary = new Cloudinary();
            }
        }

        public async Task<string?> UploadImageAsync(IFormFile file, string folder = "store_management")
        {
            Logger.Debug("UploadImageAsync started. FileName: {0}, Folder: {1}", file?.FileName, folder);
            if (file == null || file.Length == 0)
            {
                Logger.Error("UploadImageAsync failed. Reason: File is missing or empty.");
                return null;
            }

            using var stream = file.OpenReadStream();
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = folder,
                Transformation = new Transformation().Quality("auto").FetchFormat("auto")
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            if (uploadResult.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var imageUrl = uploadResult.SecureUrl?.ToString() ?? uploadResult.Url?.ToString();
                Logger.Info("UploadImageAsync succeeded. FileName: {0}, Folder: {1}", file.FileName, folder);
                return imageUrl;
            }

            var error = uploadResult.Error?.Message ?? "Cloudinary did not return a success status.";
            Logger.Error("UploadImageAsync failed. Reason: {0}", error);
            throw new InvalidOperationException($"Cloudinary upload failed: {error}");
        }

        public async Task<bool> DeleteImageAsync(string publicId)
        {
            Logger.Debug("DeleteImageAsync started. PublicId: {0}", publicId);
            if (string.IsNullOrEmpty(publicId))
            {
                Logger.Error("DeleteImageAsync failed. Reason: Public ID is missing.");
                return false;
            }

            var deleteParams = new DeletionParams(publicId);
            var result = await _cloudinary.DestroyAsync(deleteParams);
            var succeeded = result.Result == "ok";
            if (succeeded)
                Logger.Info("DeleteImageAsync succeeded. PublicId: {0}", publicId);
            else
                Logger.Error("DeleteImageAsync failed. Reason: Cloudinary returned {0}. PublicId: {1}", result.Result, publicId);
            return succeeded;
        }
    }
}
