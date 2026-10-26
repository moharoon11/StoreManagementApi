using SubscriptionPlatform.Api.Models;
using SubscriptionPlatform.Api.Repositories;

namespace SubscriptionPlatform.Api.Services;

public sealed class CatalogService(CatalogRepository repository, ILogger<CatalogService> logger)
{
    public async Task<Guid> CreatePlanAsync(Guid appId, PlanRequest request)
    {
        ValidatePlan(request);
        try
        {
            if (!await repository.IsApplicationActiveAsync(appId))
            {
                throw new UnauthorizedAccessException("Application is inactive.");
            }

            return await repository.CreatePlanAsync(appId, request);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plan creation failed for application {ApplicationId}", appId);
            throw;
        }
    }

    public Task<IEnumerable<object>> ListPlansAsync(Guid appId)
    {
        return repository.ListPlansAsync(appId);
    }

    public Task<object?> GetPlanAsync(Guid appId, Guid planId)
    {
        return repository.GetPlanAsync(appId, planId);
    }

    public async Task<object> CreateCustomerAsync(Guid appId, CustomerRequest request)
    {
        var emailIsValid = string.IsNullOrWhiteSpace(request.Email)
            || new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email);
        if (string.IsNullOrWhiteSpace(request.ExternalId)
            || request.ExternalId.Length > 200
            || !emailIsValid)
        {
            throw new ArgumentException("Customer external ID or email is invalid.");
        }

        try
        {
            if (!await repository.IsApplicationActiveAsync(appId))
            {
                throw new UnauthorizedAccessException("Application is inactive.");
            }

            return await repository.CreateCustomerAsync(appId, request);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Customer create failed for application {ApplicationId}", appId);
            throw;
        }
    }

    public Task<IEnumerable<object>> ListCustomersAsync(Guid appId)
    {
        return repository.ListCustomersAsync(appId);
    }

    private static void ValidatePlan(PlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            || request.Name.Length > 120
            || request.Currency.Length != 3
            || request.MonthlyAmountMinor < 0
            || request.YearlyAmountMinor < 0
            || request.ThreeMonthAmountMinor < 0
            || request.SixMonthAmountMinor < 0
            || request.TrialDays is < 0 or > 90)
        {
            throw new ArgumentException("Plan fields are invalid.");
        }
    }
}
