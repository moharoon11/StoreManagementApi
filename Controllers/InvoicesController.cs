using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            var history = await _invoiceRepository.GetInvoiceHistoryAsync(CurrentUserId, pageNumber, pageSize, fromDate, toDate);
            return Ok(ApiResponse<PagedResponse<Invoice>>.SuccessResult(history, "Invoice history retrieved."));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var invoice = await _invoiceRepository.GetByIdAsync(id, CurrentUserId);
            if (invoice == null)
            {
                return NotFound(ApiResponse.ErrorResult("Invoice not found."));
            }

            return Ok(ApiResponse<Invoice>.SuccessResult(invoice, "Invoice details retrieved."));
        }

        [HttpGet("{id:int}/pdf")]
        public async Task<IActionResult> DownloadPdf(int id)
        {
            var invoice = await _invoiceRepository.GetByIdAsync(id, CurrentUserId);
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
