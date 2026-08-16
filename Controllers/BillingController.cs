using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using StoreManagement.Api.Dtos;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;
using StoreManagement.Api.Repositories;

namespace StoreManagement.Api.Controllers
{
    [Authorize]
    [Route("api/billing")]
    public class BillingController : BaseApiController
    {
        private static readonly Logger Logger = LogManager.GetLogger("BillingController");
        private readonly IBillingRepository _billingRepository;

        public BillingController(IBillingRepository billingRepository)
        {
            _billingRepository = billingRepository;
        }

        [HttpPost("checkout")]
        public async Task<IActionResult> Checkout([FromBody] CheckoutRequestDto request)
        {
            Logger.Debug("Checkout started.");
            if (!ModelState.IsValid ||
                string.IsNullOrWhiteSpace(request.CustomerName) ||
                string.IsNullOrWhiteSpace(request.CustomerMobileNumber) ||
                request.Items == null ||
                !request.Items.Any())
            {
                return BadRequest(ApiResponse.ErrorResult(
                    "Provide a valid customer name, customer mobile number, and at least one product."));
            }

            var invoice = await _billingRepository.ProcessCheckoutAsync(CurrentUserId, request);
            return Ok(ApiResponse<Invoice>.SuccessResult(invoice, "Checkout completed and invoice generated successfully."));
        }
    }
}
