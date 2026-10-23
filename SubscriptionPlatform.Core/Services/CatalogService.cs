using SubscriptionPlatform.Api.Models;
using SubscriptionPlatform.Api.Repositories;

namespace SubscriptionPlatform.Api.Services;

public sealed class CatalogService(CatalogRepository repository,ILogger<CatalogService> logger)
{
    public async Task<Guid> CreatePlanAsync(Guid appId,PlanRequest r){ValidatePlan(r);try{if(!await repository.IsApplicationActiveAsync(appId))throw new UnauthorizedAccessException("Application is inactive.");return await repository.CreatePlanAsync(appId,r);}catch(Exception ex){logger.LogError(ex,"Plan creation failed for application {ApplicationId}",appId);throw;}}
    public Task<IEnumerable<object>> ListPlansAsync(Guid appId)=>repository.ListPlansAsync(appId);
    public Task<object?> GetPlanAsync(Guid appId,Guid planId)=>repository.GetPlanAsync(appId,planId);
    public async Task<object> CreateCustomerAsync(Guid appId,CustomerRequest r){if(string.IsNullOrWhiteSpace(r.ExternalId)||r.ExternalId.Length>200||(!string.IsNullOrWhiteSpace(r.Email)&&!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(r.Email)))throw new ArgumentException("Customer external ID or email is invalid.");try{if(!await repository.IsApplicationActiveAsync(appId))throw new UnauthorizedAccessException("Application is inactive.");return await repository.CreateCustomerAsync(appId,r);}catch(Exception ex){logger.LogError(ex,"Customer create failed for application {ApplicationId}",appId);throw;}}
    public Task<IEnumerable<object>> ListCustomersAsync(Guid appId)=>repository.ListCustomersAsync(appId);
    private static void ValidatePlan(PlanRequest r){if(string.IsNullOrWhiteSpace(r.Name)||r.Name.Length>120||r.Currency.Length!=3||r.MonthlyAmountMinor<0||r.YearlyAmountMinor<0||r.ThreeMonthAmountMinor<0||r.SixMonthAmountMinor<0||r.TrialDays is <0 or >90)throw new ArgumentException("Plan fields are invalid.");}
}
