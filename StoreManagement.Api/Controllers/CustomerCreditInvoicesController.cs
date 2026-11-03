using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using StoreManagement.Api.Dtos;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Models;
using StoreManagement.Api.Repositories;

namespace StoreManagement.Api.Controllers;

[Authorize]
[Route("api/customer-credit-invoices")]
public class CustomerCreditInvoicesController(ICustomerCreditInvoiceRepository repository) : BaseApiController
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool pendingOnly = true)
    {
        try
        {
            Logger.Debug(this.MethodName() + " | Loading credit invoices. UserId: {0}, PendingOnly: {1}", CurrentUserId, pendingOnly);
            return Ok(ApiResponse<IReadOnlyList<CustomerCreditInvoice>>.SuccessResult(await repository.GetAllAsync(CurrentUserId, pendingOnly)));
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(GetAll));
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            Logger.Debug(this.MethodName() + " | Loading credit invoice. UserId: {0}, InvoiceId: {1}", CurrentUserId, id);
            var invoice = await repository.GetByIdAsync(id, CurrentUserId);
            if (invoice is null)
                Logger.Debug(this.MethodName() + " | Credit invoice not found. InvoiceId: {0}", id);
            return invoice is null ? NotFound(ApiResponse.ErrorResult("Customer credit invoice not found.")) : Ok(ApiResponse<CustomerCreditInvoice>.SuccessResult(invoice));
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(GetById));
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCustomerCreditInvoiceDto dto)
    {
        try
        {
            if (!ModelState.IsValid || !OptionalProductIsValid(dto.ProductName, dto.Quantity, dto.Price) || !ItemsAreValid(dto.Items))
            {
                Logger.Debug(this.MethodName() + " | Credit invoice validation failed.");
                return BadRequest(ApiResponse.ErrorResult("Provide product name, quantity, and price together, with positive quantity and price."));
            }
            var invoice = await repository.CreateAsync(CurrentUserId, dto);
            Logger.Info(this.MethodName() + " | Credit invoice created. UserId: {0}, InvoiceId: {1}", CurrentUserId, invoice.Id);
            return CreatedAtAction(nameof(GetById), new { id = invoice.Id }, ApiResponse<CustomerCreditInvoice>.SuccessResult(invoice, "Customer credit invoice created."));
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Create));
        }
    }

    [HttpPost("{id:int}/transactions")]
    public async Task<IActionResult> AddTransaction(int id, [FromBody] AddCustomerCreditTransactionDto dto)
    {
        try
        {
            if (!ModelState.IsValid || !OptionalProductIsValid(dto.ProductName, dto.Quantity, dto.Price) || !ItemsAreValid(dto.Items))
            {
                Logger.Debug(this.MethodName() + " | Credit transaction validation failed. InvoiceId: {0}", id);
                return BadRequest(ApiResponse.ErrorResult("Provide product name, quantity, and price together, with positive quantity and price."));
            }
            var invoice = await repository.AddTransactionAsync(id, CurrentUserId, dto);
            if (invoice is not null)
                Logger.Info(this.MethodName() + " | Credit transaction added. UserId: {0}, InvoiceId: {1}", CurrentUserId, id);
            else
                Logger.Debug(this.MethodName() + " | Pending credit invoice not found. InvoiceId: {0}", id);
            return invoice is null ? NotFound(ApiResponse.ErrorResult("Pending customer credit invoice not found.")) : Ok(ApiResponse<CustomerCreditInvoice>.SuccessResult(invoice, "Transaction added."));
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(AddTransaction));
        }
    }

    [HttpPut("{id:int}/received")]
    public async Task<IActionResult> MarkReceived(int id)
    {
        try
        {
            var updated = await repository.MarkReceivedAsync(id, CurrentUserId);
            if (updated)
                Logger.Info(this.MethodName() + " | Credit invoice marked received. UserId: {0}, InvoiceId: {1}", CurrentUserId, id);
            else
                Logger.Debug(this.MethodName() + " | Pending credit invoice not found. InvoiceId: {0}", id);
            return updated ? Ok(ApiResponse.SuccessResult("Invoice marked as received.")) : NotFound(ApiResponse.ErrorResult("Pending customer credit invoice not found."));
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(MarkReceived));
        }
    }

    [HttpPut("{id:int}/transactions/{transactionId:int}/received")]
    public async Task<IActionResult> MarkTransactionReceived(int id, int transactionId)
    {
        try
        {
            var updated = await repository.MarkTransactionReceivedAsync(id, transactionId, CurrentUserId);
            if (updated)
                Logger.Info(this.MethodName() + " | Credit transaction marked received. UserId: {0}, InvoiceId: {1}, TransactionId: {2}", CurrentUserId, id, transactionId);
            else
                Logger.Debug(this.MethodName() + " | Pending credit entry not found. InvoiceId: {0}, TransactionId: {1}", id, transactionId);
            return updated ? Ok(ApiResponse.SuccessResult("Credit entry marked as received.")) : NotFound(ApiResponse.ErrorResult("Pending credit entry not found."));
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(MarkTransactionReceived));
        }
    }

    [HttpPost("{id:int}/transactions/{transactionId:int}/payments")]
    public async Task<IActionResult> RecordTransactionPayment(
        int id,
        int transactionId,
        [FromBody] RecordCustomerCreditPaymentDto dto)
    {
        try
        {
            if (!ModelState.IsValid || dto is null)
            {
                Logger.Debug(this.MethodName() + " | Credit payment validation failed. InvoiceId: {0}, TransactionId: {1}", id, transactionId);
                return BadRequest(ApiResponse.ErrorResult("Enter a valid payment amount."));
            }

            var updated = await repository.RecordTransactionPaymentAsync(
                id, transactionId, CurrentUserId, dto.Amount);
            if (updated)
                Logger.Info(this.MethodName() + " | Credit payment recorded. UserId: {0}, InvoiceId: {1}, TransactionId: {2}", CurrentUserId, id, transactionId);
            else
                Logger.Warn(this.MethodName() + " | Credit payment rejected: amount exceeds balance or entry settled. InvoiceId: {0}, TransactionId: {1}", id, transactionId);
            return updated
                ? Ok(ApiResponse.SuccessResult("Partial payment recorded."))
                : BadRequest(ApiResponse.ErrorResult("Payment exceeds the remaining credit amount, or this entry is already settled."));
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(RecordTransactionPayment));
        }
    }

    private static bool OptionalProductIsValid(string? name, decimal? quantity, decimal? price)
    {
        var hasAny = !string.IsNullOrWhiteSpace(name) || quantity.HasValue || price.HasValue;
        return !hasAny || (!string.IsNullOrWhiteSpace(name) && quantity > 0 && price > 0);
    }

    private static bool ItemsAreValid(IReadOnlyCollection<CreditLineDto>? items) =>
        items is null || items.All(item => !string.IsNullOrWhiteSpace(item.ProductName) && item.Quantity > 0 && item.Price > 0);
}
