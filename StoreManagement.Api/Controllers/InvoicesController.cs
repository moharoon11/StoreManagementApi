using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;
using StoreManagement.Api.Repositories;
using StoreManagement.Api.Services;

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
            Logger.Debug("GetInvoiceHistory started. PageNumber: {0}, PageSize: {1}", pageNumber, pageSize);
            var history = await _invoiceRepository.GetInvoiceHistoryAsync(CurrentUserId, pageNumber, pageSize, fromDate, toDate);
            return Ok(ApiResponse<PagedResponse<Invoice>>.SuccessResult(history, "Invoice history retrieved."));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            Logger.Debug("GetById started. InvoiceId: {0}", id);
            var invoice = await _invoiceRepository.GetByIdAsync(id, CurrentUserId);
            if (invoice == null)
            {
                return NotFound(ApiResponse.ErrorResult("Invoice not found."));
            }

            return Ok(ApiResponse<Invoice>.SuccessResult(invoice, "Invoice details retrieved."));
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary([FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null)
        {
            Logger.Debug("GetSummary started.");
            var summary = await _invoiceRepository.GetInvoiceSummaryAsync(CurrentUserId, fromDate, toDate);
            return Ok(ApiResponse<InvoiceSummaryDto>.SuccessResult(summary, "Invoice summary retrieved."));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] Dtos.UpdateInvoiceDto dto)
        {
            Logger.Debug("Update invoice started. InvoiceId: {0}", id);
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

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            Logger.Debug("Delete invoice started. InvoiceId: {0}", id);
            var deleted = await _invoiceRepository.DeleteInvoiceAsync(id, CurrentUserId);
            if (!deleted)
            {
                return NotFound(ApiResponse.ErrorResult("Invoice not found or delete failed."));
            }

            return Ok(ApiResponse.SuccessResult("Invoice deleted successfully."));
        }

        [AllowAnonymous]
        [HttpGet("{id:int}/pdf")]
        public async Task<IActionResult> DownloadPdf(int id, [FromServices] IPdfService pdfService)
        {
            Logger.Debug("DownloadPdf api started. InvoiceId: {0}", id);
            var invoice = await _invoiceRepository.GetByIdAsync(id, 0);
            if (invoice == null)
            {
                Logger.Debug($"{nameof(DownloadPdf)} |  Pdf not found for downloading | pdf is null for id = {id}");
                return NotFound(ApiResponse.ErrorResult("Invoice not found."));
            }

            var pdfBytes = pdfService.GenerateInvoicePdf(invoice);
            var fileName = $"Invoice_{invoice.InvoiceNumber}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }
    }
}
