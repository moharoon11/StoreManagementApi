using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using StoreManagement.Api.Helpers;
using SubscriptionPlatform.Api.Models;
using SubscriptionPlatform.Api.Services;

namespace StoreManagement.Api.Controllers;

/// <summary>
/// Development-only plan management endpoints for Postman and local administration.
/// Keep SubscriptionPlatform:EnablePlanManagement=false in production.
/// </summary>
[ApiController]
[Route("api/dev/plans")]
public sealed class DevelopmentPlansController(
    CatalogService catalog,
    IConfiguration configuration) : ControllerBase
{
    private static readonly Logger Logger = LogManager.GetLogger("DevelopmentPlansController");
    private Guid ApplicationId => Guid.TryParse(configuration["SubscriptionPlatform:ApplicationId"], out var id)
        ? id
        : throw new InvalidOperationException("SubscriptionPlatform:ApplicationId must be configured.");

    private void EnsureEnabled()
    {
        if (!configuration.GetValue<bool>("SubscriptionPlatform:EnablePlanManagement"))
            throw new InvalidOperationException("Development plan management is disabled.");
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        try
        {
            EnsureEnabled();
            return Ok(ApiResponse<IEnumerable<object>>.SuccessResult(
                await catalog.ListPlansAsync(ApplicationId), "Plans loaded."));
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(List));
        }
    }

    [HttpGet("{planId:guid}")]
    public async Task<IActionResult> Get(Guid planId)
    {
        try
        {
            EnsureEnabled();
            var plan = await catalog.GetPlanAsync(ApplicationId, planId);
            return plan is null
                ? NotFound(ApiResponse.ErrorResult("Plan was not found."))
                : Ok(ApiResponse<object>.SuccessResult(plan, "Plan loaded."));
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Get));
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PlanRequest request)
    {
        try
        {
            EnsureEnabled();
            var planId = await catalog.CreatePlanAsync(ApplicationId, request);
            return Ok(ApiResponse<object>.SuccessResult(new { planId }, "Plan created."));
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Create));
        }
    }

    [HttpPut("{planId:guid}")]
    public async Task<IActionResult> Update(Guid planId, [FromBody] PlanRequest request)
    {
        try
        {
            EnsureEnabled();
            var updated = await catalog.UpdatePlanAsync(ApplicationId, planId, request);
            return updated
                ? Ok(ApiResponse.SuccessResult("Plan updated."))
                : NotFound(ApiResponse.ErrorResult("Plan was not found."));
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Update));
        }
    }

    [HttpDelete("{planId:guid}")]
    public async Task<IActionResult> Delete(Guid planId)
    {
        try
        {
            EnsureEnabled();
            var deleted = await catalog.DeletePlanAsync(ApplicationId, planId);
            return deleted
                ? Ok(ApiResponse.SuccessResult("Plan disabled."))
                : NotFound(ApiResponse.ErrorResult("Plan was not found."));
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Delete));
        }
    }
}
