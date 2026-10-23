using Dapper;
using SubscriptionPlatform.Api.Data;
using SubscriptionPlatform.Api.Models;

namespace SubscriptionPlatform.Api.Repositories;

public sealed class CatalogRepository(ConnectionFactory connections)
{
    public async Task<bool> IsApplicationActiveAsync(Guid id){await using var db=connections.Create();return await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM registered_applications WHERE id=@id AND active=1",new{id})>0;}
    public async Task<Guid> CreatePlanAsync(Guid appId,PlanRequest r){var id=Guid.NewGuid();await using var db=connections.Create();await db.ExecuteAsync("INSERT INTO plans(id,application_id,name,currency,monthly_amount_minor,yearly_amount_minor,three_month_amount_minor,six_month_amount_minor,trial_days,features_json,limits_json,active) VALUES(@id,@appId,@Name,@Currency,@MonthlyAmountMinor,@YearlyAmountMinor,@ThreeMonthAmountMinor,@SixMonthAmountMinor,@TrialDays,@FeaturesJson,@LimitsJson,1)",new{id,appId,r.Name,Currency=r.Currency.ToUpperInvariant(),r.MonthlyAmountMinor,r.YearlyAmountMinor,r.ThreeMonthAmountMinor,r.SixMonthAmountMinor,r.TrialDays,r.FeaturesJson,r.LimitsJson});return id;}
    public async Task<IEnumerable<object>> ListPlansAsync(Guid appId){await using var db=connections.Create();return await db.QueryAsync("SELECT id,name,currency,monthly_amount_minor,yearly_amount_minor,three_month_amount_minor,six_month_amount_minor,trial_days,features_json,limits_json FROM plans WHERE application_id=@appId AND active=1",new{appId});}
    public async Task<object?> GetPlanAsync(Guid appId,Guid planId){await using var db=connections.Create();return await db.QuerySingleOrDefaultAsync("SELECT id,name,currency,monthly_amount_minor,yearly_amount_minor,three_month_amount_minor,six_month_amount_minor,trial_days,features_json,limits_json FROM plans WHERE application_id=@appId AND id=@planId AND active=1",new{appId,planId});}
    public async Task<object> CreateCustomerAsync(Guid appId,CustomerRequest r){var id=Guid.NewGuid();await using var db=connections.Create();await db.ExecuteAsync("INSERT INTO customers(id,application_id,external_id,email,created_utc) VALUES(@id,@appId,@ExternalId,@Email,UTC_TIMESTAMP(6)) ON DUPLICATE KEY UPDATE email=COALESCE(VALUES(email),email)",new{id,appId,r.ExternalId,r.Email});return await db.QuerySingleAsync("SELECT id,external_id ExternalId,email FROM customers WHERE application_id=@appId AND external_id=@ExternalId",new{appId,r.ExternalId});}
    public async Task<IEnumerable<object>> ListCustomersAsync(Guid appId){await using var db=connections.Create();return await db.QueryAsync("SELECT id,external_id ExternalId,email,created_utc CreatedUtc FROM customers WHERE application_id=@appId",new{appId});}
}
