using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreManagement.Api.Dtos;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Repositories;

namespace StoreManagement.Api.Controllers
{
    [Authorize]
    [Route("api/reports")]
    public class ReportsController : BaseApiController
    {
        private readonly IReportRepository _reportRepository;

        public ReportsController(IReportRepository reportRepository)
        {
            _reportRepository = reportRepository;
        }

        [HttpGet("sales")]
        public async Task<IActionResult> GetSalesReport([FromQuery] SalesReportFilterDto filter)
        {
            Logger.Debug("GetSalesReport started.");
            var report = await _reportRepository.GetSalesReportAsync(CurrentUserId, filter);
            return Ok(ApiResponse<SalesReportDto>.SuccessResult(report, "Sales report retrieved successfully."));
        }
    }
}
