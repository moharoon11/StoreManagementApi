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
        private readonly IPdfService _pdfService;

        public InvoicesController(IInvoiceRepository invoiceRepository, IPdfService pdfService)
        {
            _invoiceRepository = invoiceRepository;
            _pdfService = pdfService;
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

        [AllowAnonymous]
        [HttpGet("{id:int}/pdf")]
        public async Task<IActionResult> DownloadPdf(int id)
        {
            Logger.Debug("DownloadPdf started. InvoiceId: {0}", id);
            var invoice = await _invoiceRepository.GetByIdAsync(id, 0);
            if (invoice == null)
            {
                return NotFound(ApiResponse.ErrorResult("Invoice not found."));
            }

            var pdfBytes = _pdfService.GenerateInvoicePdf(invoice);
            var fileName = $"Invoice_{invoice.InvoiceNumber}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }
    }
}
