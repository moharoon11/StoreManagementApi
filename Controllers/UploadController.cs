using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Services;

namespace StoreManagement.Api.Controllers
{
    [Authorize]
    [Route("api/upload")]
    public class UploadController : BaseApiController
    {
        private readonly ICloudinaryService _cloudinaryService;

        public UploadController(ICloudinaryService cloudinaryService)
        {
            _cloudinaryService = cloudinaryService;
        }

        [HttpPost("image")]
        public async Task<IActionResult> UploadImage(IFormFile file, [FromQuery] string folder = "general")
        {
            Logger.Debug("UploadImage started. FileName: {0}, Folder: {1}", file?.FileName, folder);
            if (file == null || file.Length == 0)
            {
                return BadRequest(ApiResponse.ErrorResult("Please select an image file to upload."));
            }

            var imageUrl = await _cloudinaryService.UploadImageAsync(file, folder);
            if (string.IsNullOrEmpty(imageUrl))
            {
                return BadRequest(ApiResponse.ErrorResult("Image upload failed."));
            }

            return Ok(ApiResponse<object>.SuccessResult(new { url = imageUrl }, "Image uploaded successfully to Cloudinary."));
        }
    }
}
