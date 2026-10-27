using SubscriptionPlatform.Api.Models;
using SubscriptionPlatform.Api.Repositories;
using NLog;

namespace SubscriptionPlatform.Api.Services;

public sealed class CatalogService(CatalogRepository repository)
{
    private static readonly Logger Logger = LogManager.GetLogger("SubscriptionPlatform.Services.CatalogService");
    public async Task<Guid> CreatePlanAsync(Guid appId, PlanRequest request)
    {
        Logger.Debug($"{nameof(CreatePlanAsync)} | ApplicationId: {appId} | Payload: PlanName={request.Name}, Currency={request.Currency}");
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
            Logger.Error(ex, $"{nameof(CreatePlanAsync)} | ApplicationId: {appId} | Exception: {ex.Message}");
            throw;
        }
    }

    public Task<IEnumerable<object>> ListPlansAsync(Guid appId)
    {
        return repository.ListPlansAsync(appId);
    }

    public Task<object?> GetPlanAsync(Guid appId, Guid planId)
    {
        Logger.Debug($"{nameof(GetPlanAsync)} | ApplicationId: {appId} | PlanId: {planId}");
        return repository.GetPlanAsync(appId, planId);
    }

    public async Task<object> CreateCustomerAsync(Guid appId, CustomerRequest request)
    {
        Logger.Debug($"{nameof(CreateCustomerAsync)} | ApplicationId: {appId} | Payload: ExternalId={request.ExternalId}");
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
            Logger.Error(ex, $"{nameof(CreateCustomerAsync)} | ApplicationId: {appId} | Exception: {ex.Message}");
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
