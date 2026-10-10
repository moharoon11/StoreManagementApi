using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreManagement.Api.Dtos;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;
using StoreManagement.Api.Repositories;

namespace StoreManagement.Api.Controllers;

[Authorize]
[Route("api/customer-credit-invoices")]
public class CustomerCreditInvoicesController(ICustomerCreditInvoiceRepository repository) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool pendingOnly = true) =>
        Ok(ApiResponse<IReadOnlyList<CustomerCreditInvoice>>.SuccessResult(await repository.GetAllAsync(CurrentUserId, pendingOnly)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var invoice = await repository.GetByIdAsync(id, CurrentUserId);
        return invoice is null ? NotFound(ApiResponse.ErrorResult("Customer credit invoice not found.")) : Ok(ApiResponse<CustomerCreditInvoice>.SuccessResult(invoice));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCustomerCreditInvoiceDto dto)
    {
        if (!ModelState.IsValid || !OptionalProductIsValid(dto.ProductName, dto.Quantity, dto.Price))
            return BadRequest(ApiResponse.ErrorResult("Provide product name, quantity, and price together, with positive quantity and price."));
        var invoice = await repository.CreateAsync(CurrentUserId, dto);
        return CreatedAtAction(nameof(GetById), new { id = invoice.Id }, ApiResponse<CustomerCreditInvoice>.SuccessResult(invoice, "Customer credit invoice created."));
    }

    [HttpPost("{id:int}/transactions")]
    public async Task<IActionResult> AddTransaction(int id, [FromBody] AddCustomerCreditTransactionDto dto)
    {
        if (!ModelState.IsValid || !OptionalProductIsValid(dto.ProductName, dto.Quantity, dto.Price))
            return BadRequest(ApiResponse.ErrorResult("Provide product name, quantity, and price together, with positive quantity and price."));
        var invoice = await repository.AddTransactionAsync(id, CurrentUserId, dto);
        return invoice is null ? NotFound(ApiResponse.ErrorResult("Pending customer credit invoice not found.")) : Ok(ApiResponse<CustomerCreditInvoice>.SuccessResult(invoice, "Transaction added."));
    }

    [HttpPut("{id:int}/received")]
    public async Task<IActionResult> MarkReceived(int id)
    {
        var updated = await repository.MarkReceivedAsync(id, CurrentUserId);
        return updated ? Ok(ApiResponse.SuccessResult("Invoice marked as received.")) : NotFound(ApiResponse.ErrorResult("Pending customer credit invoice not found."));
    }

    private static bool OptionalProductIsValid(string? name, decimal? quantity, decimal? price)
    {
        var hasAny = !string.IsNullOrWhiteSpace(name) || quantity.HasValue || price.HasValue;
        return !hasAny || (!string.IsNullOrWhiteSpace(name) && quantity > 0 && price > 0);
    }
}
