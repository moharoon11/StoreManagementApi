using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using StoreManagement.Api.Dtos;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Repositories;

namespace StoreManagement.Api.Controllers
{
    [Authorize]
    [Route("api/dashboard")]
    public class DashboardController : BaseApiController
    {
        private static readonly Logger Logger = LogManager.GetLogger("DashboardController");
        private readonly IDashboardRepository _dashboardRepository;

        public DashboardController(IDashboardRepository dashboardRepository)
        {
            _dashboardRepository = dashboardRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetDashboard()
        {
            try
            {
                Logger.Debug(this.MethodName() + " | GetDashboard started.");
                var summary = await _dashboardRepository.GetDashboardSummaryAsync(CurrentUserId);
                return Ok(ApiResponse<DashboardSummaryDto>.SuccessResult(summary, "Dashboard statistics retrieved."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(GetDashboard));
            }
        }
    }
}
