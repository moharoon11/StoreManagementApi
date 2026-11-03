using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;
using StoreManagement.Api.Repositories;

namespace StoreManagement.Api.Controllers
{
    [Authorize]
    [Route("api/invoices")]
    public class InvoicesController : BaseApiController
    {
        private static readonly Logger Logger = LogManager.GetLogger("InvoicesController");
        private readonly IInvoiceRepository _invoiceRepository;

        public InvoicesController(IInvoiceRepository invoiceRepository)
        {
            _invoiceRepository = invoiceRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetInvoiceHistory(
            [FromQuery] int pageNumber = 1, 
            [FromQuery] int pageSize = 10,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            try
            {
                Logger.Debug(this.MethodName() + " | GetInvoiceHistory started. PageNumber: {0}, PageSize: {1}", pageNumber, pageSize);
                var history = await _invoiceRepository.GetInvoiceHistoryAsync(CurrentUserId, pageNumber, pageSize, fromDate, toDate);
                return Ok(ApiResponse<PagedResponse<Invoice>>.SuccessResult(history, "Invoice history retrieved."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(GetInvoiceHistory));
            }
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                Logger.Debug(this.MethodName() + " | GetById started. InvoiceId: {0}", id);
                var invoice = await _invoiceRepository.GetByIdAsync(id, CurrentUserId);
                if (invoice == null)
                {
                    return NotFound(ApiResponse.ErrorResult("Invoice not found."));
                }

                return Ok(ApiResponse<Invoice>.SuccessResult(invoice, "Invoice details retrieved."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(GetById));
            }
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary([FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null)
        {
            try
            {
                Logger.Debug(this.MethodName() + " | GetSummary started.");
                var summary = await _invoiceRepository.GetInvoiceSummaryAsync(CurrentUserId, fromDate, toDate);
                return Ok(ApiResponse<InvoiceSummaryDto>.SuccessResult(summary, "Invoice summary retrieved."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(GetSummary));
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] Dtos.UpdateInvoiceDto dto)
        {
            try
            {
                Logger.Debug(this.MethodName() + " | Update invoice started. InvoiceId: {0}", id);
                if (!ModelState.IsValid || dto == null)
                {
                    return BadRequest(ApiResponse.ErrorResult("Invalid invoice update data."));
                }

                var updated = await _invoiceRepository.UpdateInvoiceAsync(id, CurrentUserId, dto);
                if (!updated)
                {
                    return NotFound(ApiResponse.ErrorResult("Invoice not found or update failed."));
                }

                return Ok(ApiResponse.SuccessResult("Invoice updated successfully."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Update));
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                Logger.Debug(this.MethodName() + " | Delete invoice started. InvoiceId: {0}", id);
                var deleted = await _invoiceRepository.DeleteInvoiceAsync(id, CurrentUserId);
                if (!deleted)
                {
                    return NotFound(ApiResponse.ErrorResult("Invoice not found or delete failed."));
                }

                return Ok(ApiResponse.SuccessResult("Invoice deleted successfully."));
            }
            catch (Exception ex)
            {
                return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Delete));
            }
        }
    }
}
