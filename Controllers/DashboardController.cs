using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreManagement.Api.Dtos;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Repositories;

namespace StoreManagement.Api.Controllers
{
    [Authorize]
    [Route("api/dashboard")]
    public class DashboardController : BaseApiController
    {
        private readonly IDashboardRepository _dashboardRepository;

        public DashboardController(IDashboardRepository dashboardRepository)
        {
            _dashboardRepository = dashboardRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetDashboard()
        {
            Logger.Debug("GetDashboard started.");
            var summary = await _dashboardRepository.GetDashboardSummaryAsync(CurrentUserId);
            return Ok(ApiResponse<DashboardSummaryDto>.SuccessResult(summary, "Dashboard statistics retrieved."));
        }
    }
}
