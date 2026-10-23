using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using StoreManagement.Api.Dtos;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;
using StoreManagement.Api.Repositories;
using StoreManagement.Api.Services;

namespace StoreManagement.Api.Controllers
{
    [Authorize]
    [Route("api/store/profile")]
    public class StoreProfileController : BaseApiController
    {
        private static readonly Logger Logger = LogManager.GetLogger("StoreProfileController");
        private readonly IStoreProfileRepository _storeProfileRepository;
        private readonly ICloudinaryService _cloudinaryService;

        public StoreProfileController(IStoreProfileRepository storeProfileRepository, ICloudinaryService cloudinaryService)
        {
            _storeProfileRepository = storeProfileRepository;
            _cloudinaryService = cloudinaryService;
        }

        [HttpGet]
        public async Task<IActionResult> GetProfile()
        {
            Logger.Debug("GetProfile started.");
            var profile = await _storeProfileRepository.GetByUserIdAsync(CurrentUserId);
            if (profile == null)
            {
                return NotFound(ApiResponse.ErrorResult("Store profile not created yet."));
            }

            return Ok(ApiResponse<StoreProfile>.SuccessResult(profile, "Store profile retrieved."));
        }

        [HttpPost]
        public async Task<IActionResult> CreateOrUpdateProfile([FromBody] CreateOrUpdateStoreProfileDto dto)
        {
            Logger.Debug("CreateOrUpdateProfile started. StoreName: {0}", dto?.StoreName);
            if (!ModelState.IsValid || dto == null)
            {
                return BadRequest(ApiResponse.ErrorResult("Invalid store profile data."));
            }

            string? logoUrl = dto.ExistingLogoUrl;

            if (dto.LogoFile != null && dto.LogoFile.Length > 0)
            {
                logoUrl = await _cloudinaryService.UploadImageAsync(dto.LogoFile, "store_logos");
            }

            var profile = new StoreProfile
            {
                UserId = CurrentUserId,
                StoreName = dto.StoreName,
                OwnerName = dto.OwnerName,
                LogoUrl = logoUrl,
                Address = dto.Address,
                City = dto.City,
                District = dto.District,
                Pincode = dto.Pincode,
                Email = dto.Email,
                GstNumber = dto.GstNumber,
                Phone = dto.Phone,
                AlternatePhone = dto.AlternatePhone,
                AboutUs = dto.AboutUs
            };

            await _storeProfileRepository.CreateOrUpdateProfileAsync(profile);
            var updatedProfile = await _storeProfileRepository.GetByUserIdAsync(CurrentUserId);

            return Ok(ApiResponse<StoreProfile>.SuccessResult(updatedProfile!, "Store profile saved successfully."));
        }

        [HttpPut]
        public async Task<IActionResult> UpdateProfile([FromBody] CreateOrUpdateStoreProfileDto dto)
        {
            Logger.Debug("UpdateProfile started.");
            return await CreateOrUpdateProfile(dto);
        }
    }
}
