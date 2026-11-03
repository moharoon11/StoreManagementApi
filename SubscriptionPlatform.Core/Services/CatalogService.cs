using SubscriptionPlatform.Api.Models;
using SubscriptionPlatform.Api.Repositories;
using NLog;

namespace SubscriptionPlatform.Api.Services;

public sealed class CatalogService(CatalogRepository repository)
{
    private static readonly Logger Logger = LogManager.GetLogger("SubscriptionPlatform.Services.CatalogService");
    public async Task<Guid> CreatePlanAsync(Guid appId, PlanRequest request)
    {
        Logger.Debug($"{this.MethodName()} | ApplicationId: {appId} | Payload: PlanName={request.Name}, Currency={request.Currency}");
        ValidatePlan(request);
        try
        {
            if (!await repository.IsApplicationActiveAsync(appId))
            {
                throw new UnauthorizedAccessException("Application is inactive.");
            }

            var planId = await repository.CreatePlanAsync(appId, request);
            Logger.Info(this.MethodName() + " | Plan created. ApplicationId: {0}, PlanId: {1}", appId, planId);
            return planId;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"{this.MethodName()} | ApplicationId: {appId} | Exception: {ex.Message}");
            throw;
        }
    }

    public async Task<IEnumerable<object>> ListPlansAsync(Guid appId)
    {
        try
        {
            var plans = (await repository.ListPlansAsync(appId)).ToList();
            Logger.Debug(this.MethodName() + " | Plan list returned. ApplicationId: {0}, Count: {1}", appId, plans.Count);
            return plans;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Service operation failed.");
            throw;
        }
    }

    public async Task<object?> GetPlanAsync(Guid appId, Guid planId)
    {
        try
        {
            Logger.Debug($"{this.MethodName()} | ApplicationId: {appId} | PlanId: {planId}");
            var plan = await repository.GetPlanAsync(appId, planId);
            Logger.Debug(this.MethodName() + " | Plan found: {0}. ApplicationId: {1}, PlanId: {2}", plan is not null, appId, planId);
            return plan;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Service operation failed.");
            throw;
        }
    }

    public async Task<object> CreateCustomerAsync(Guid appId, CustomerRequest request)
    {
        Logger.Debug($"{this.MethodName()} | ApplicationId: {appId} | Payload: ExternalId={request.ExternalId}");
        var emailIsValid = string.IsNullOrWhiteSpace(request.Email)
            || new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email);
        if (string.IsNullOrWhiteSpace(request.ExternalId)
            || request.ExternalId.Length > 200
            || !emailIsValid)
        {
            Logger.Debug(this.MethodName() + " | Customer request validation failed. ApplicationId: {0}", appId);
            throw new ArgumentException("Customer external ID or email is invalid.");
        }

        try
        {
            if (!await repository.IsApplicationActiveAsync(appId))
            {
                throw new UnauthorizedAccessException("Application is inactive.");
            }

            var customer = await repository.CreateCustomerAsync(appId, request);
            Logger.Info(this.MethodName() + " | Customer saved. ApplicationId: {0}, ExternalId: {1}", appId, request.ExternalId);
            return customer;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"{this.MethodName()} | ApplicationId: {appId} | Exception: {ex.Message}");
            throw;
        }
    }

    public async Task<IEnumerable<object>> ListCustomersAsync(Guid appId)
    {
        try
        {
            var customers = (await repository.ListCustomersAsync(appId)).ToList();
            Logger.Debug(this.MethodName() + " | Customer list returned. ApplicationId: {0}, Count: {1}", appId, customers.Count);
            return customers;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Service operation failed.");
            throw;
        }
    }

    private void ValidatePlan(PlanRequest request)
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
            Logger.Debug(this.MethodName() + " | Plan request validation failed.");
            throw new ArgumentException("Plan fields are invalid.");
        }
    }
}
