using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Dapper;
using NLog;
using StoreManagement.Api.Data;

namespace StoreManagement.Api.Services
{
    public class CloudinaryService : ICloudinaryService
    {
        private readonly Cloudinary _cloudinary;
        private readonly IDbConnectionFactory _dbConnectionFactory;
        private readonly string? _cloudName;
        private static readonly Logger Logger = LogManager.GetLogger("CloudinaryService");

        public CloudinaryService(IConfiguration configuration, IDbConnectionFactory dbConnectionFactory)
        {
            var cloudName = configuration["Cloudinary:CloudName"];
            _cloudName = cloudName;
            _dbConnectionFactory = dbConnectionFactory;
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
            Logger.Debug(this.MethodName() + " | UploadImageAsync started. FileName: {0}, Folder: {1}", file?.FileName, folder);
            if (file == null || file.Length == 0)
            {
                Logger.Error(this.MethodName() + " | UploadImageAsync failed. Reason: File is missing or empty.");
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
                Logger.Info(this.MethodName() + " | UploadImageAsync succeeded. FileName: {0}, Folder: {1}", file.FileName, folder);
                return imageUrl;
            }

            var error = uploadResult.Error?.Message ?? "Cloudinary did not return a success status.";
            Logger.Error(this.MethodName() + " | UploadImageAsync failed. Reason: {0}", error);
            throw new InvalidOperationException($"Cloudinary upload failed: {error}");
        }

        public async Task<bool> DeleteImageAsync(string publicId)
        {
            Logger.Debug(this.MethodName() + " | DeleteImageAsync started. PublicId: {0}", publicId);
            if (string.IsNullOrEmpty(publicId))
            {
                Logger.Error(this.MethodName() + " | DeleteImageAsync failed. Reason: Public ID is missing.");
                return false;
            }

            var deleteParams = new DeletionParams(publicId);
            var result = await _cloudinary.DestroyAsync(deleteParams);
            var succeeded = result.Result == "ok";
            if (succeeded)
                Logger.Info(this.MethodName() + " | DeleteImageAsync succeeded. PublicId: {0}", publicId);
            else
                Logger.Error(this.MethodName() + " | DeleteImageAsync failed. Reason: Cloudinary returned {0}. PublicId: {1}", result.Result, publicId);
            return succeeded;
        }

        public async Task DeleteReplacedImageAsync(string? oldUrl, string? newUrl)
        {
            if (string.IsNullOrWhiteSpace(oldUrl) || string.Equals(oldUrl, newUrl, StringComparison.Ordinal))
                return;

            var oldPublicId = GetManagedPublicId(oldUrl);
            if (oldPublicId == null || string.Equals(oldPublicId, GetManagedPublicId(newUrl), StringComparison.Ordinal))
                return;

            try
            {
                using var connection = await _dbConnectionFactory.CreateConnectionAsync();
                const string sql = @"
                    SELECT EXISTS(SELECT 1 FROM Products WHERE ImageUrl = @Url)
                         + EXISTS(SELECT 1 FROM Categories WHERE ImageUrl = @Url)
                         + EXISTS(SELECT 1 FROM StoreProfiles WHERE LogoUrl = @Url);";
                var references = await connection.ExecuteScalarAsync<int>(sql, new { Url = oldUrl });
                if (references > 0)
                {
                    Logger.Debug(this.MethodName() + " | Replaced image retained because it is still referenced. PublicId: {0}, References: {1}", oldPublicId, references);
                    return;
                }

                await DeleteImageAsync(oldPublicId);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, this.MethodName() + " | Could not delete replaced Cloudinary image. PublicId: {0}", oldPublicId);
            }
        }

        private string? GetManagedPublicId(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl) || string.IsNullOrWhiteSpace(_cloudName) ||
                !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
                !string.Equals(uri.Host, "res.cloudinary.com", StringComparison.OrdinalIgnoreCase))
                return null;

            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 6 ||
                !string.Equals(segments[0], _cloudName, StringComparison.OrdinalIgnoreCase) ||
                segments[1] != "image" || segments[2] != "upload")
                return null;

            var versionIndex = Array.FindIndex(segments, 3, segment =>
                segment.Length > 1 && segment[0] == 'v' &&
                long.TryParse(segment.AsSpan(1), out _));
            if (versionIndex < 0 || versionIndex + 2 >= segments.Length ||
                segments[versionIndex + 1] is not ("general" or "product_images" or "category_images" or "store_logos" or "store_management"))
                return null;

            var publicIdWithExtension = string.Join('/', segments[(versionIndex + 1)..]);
            var extensionIndex = publicIdWithExtension.LastIndexOf('.');
            if (extensionIndex <= publicIdWithExtension.LastIndexOf('/'))
                return null;

            return Uri.UnescapeDataString(publicIdWithExtension[..extensionIndex]);
        }
    }
}
