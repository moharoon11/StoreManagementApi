using Dapper;
using NLog;
using System.Text.Json;
using SubscriptionPlatform.Api.Data;
using SubscriptionPlatform.Api.Models;

namespace SubscriptionPlatform.Api.Repositories;

public sealed class CatalogRepository(ConnectionFactory connections)
{
    private static readonly Logger Logger = LogManager.GetLogger("SubscriptionPlatform.Repositories.CatalogRepository");
    private static readonly JsonSerializerOptions PricingJsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<bool> IsApplicationActiveAsync(Guid id)
    {
        try
        {
            await using var db = await connections.OpenConnectionAsync();
            var active = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM registered_applications WHERE id=@id AND active=1",
                new { id }) > 0;
            Logger.Debug(this.MethodName() + " | Application active: {0}. ApplicationId: {1}", active, id);
            return active;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<Guid> CreatePlanAsync(Guid appId, PlanRequest request)
    {
        try
        {
            var id = Guid.NewGuid();
            await using var db = await connections.OpenConnectionAsync();
            await db.ExecuteAsync(
                "INSERT INTO plans(id,application_id,name,currency,trial_days,features_json,limits_json,pricing_json,active) VALUES(@id,@appId,@Name,@Currency,@TrialDays,@FeaturesJson,@LimitsJson,@PricingJson,1)",
                new
                {
                    id,
                    appId,
                    request.Name,
                    Currency = request.Currency.ToUpperInvariant(),
                    request.TrialDays,
                    request.FeaturesJson,
                    request.LimitsJson,
                    PricingJson = SerializePricingOptions(request.PricingOptions)
                });
            Logger.Info(this.MethodName() + " | Plan row created. ApplicationId: {0}, PlanId: {1}", appId, id);
            return id;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<IEnumerable<object>> ListPlansAsync(Guid appId)
    {
        try
        {
            Logger.Debug(this.MethodName() + " | Loading plans. ApplicationId: {0}", appId);
            await using var db = await connections.OpenConnectionAsync();
            var plans = (await db.QueryAsync(
                "SELECT id,name,currency,trial_days,features_json,limits_json,pricing_json FROM plans WHERE application_id=@appId AND active=1",
                new { appId })).AsList();
            Logger.Debug(this.MethodName() + " | Plans loaded. ApplicationId: {0}, Count: {1}", appId, plans.Count);
            return plans;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<object?> GetPlanAsync(Guid appId, Guid planId)
    {
        try
        {
            Logger.Debug(this.MethodName() + " | Loading plan. ApplicationId: {0}, PlanId: {1}", appId, planId);
            await using var db = await connections.OpenConnectionAsync();
            var plan = await db.QuerySingleOrDefaultAsync(
                "SELECT id,name,currency,trial_days,features_json,limits_json,pricing_json FROM plans WHERE application_id=@appId AND id=@planId AND active=1",
                new { appId, planId });
            Logger.Debug(this.MethodName() + " | Plan found: {0}. ApplicationId: {1}, PlanId: {2}", plan is not null, appId, planId);
            return plan;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<bool> UpdatePlanAsync(Guid appId, Guid planId, PlanRequest request)
    {
        try
        {
            await using var db = await connections.OpenConnectionAsync();
            var affected = await db.ExecuteAsync(
                "UPDATE plans SET name=@Name,currency=@Currency,trial_days=@TrialDays,features_json=@FeaturesJson,limits_json=@LimitsJson,pricing_json=@PricingJson WHERE id=@PlanId AND application_id=@AppId AND active=1",
                new
                {
                    request.Name,
                    Currency = request.Currency.ToUpperInvariant(),
                    request.TrialDays,
                    request.FeaturesJson,
                    request.LimitsJson,
                    PricingJson = SerializePricingOptions(request.PricingOptions),
                    PlanId = planId,
                    AppId = appId
                });
            return affected > 0;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<bool> DeletePlanAsync(Guid appId, Guid planId)
    {
        try
        {
            await using var db = await connections.OpenConnectionAsync();
            return await db.ExecuteAsync(
                "UPDATE plans SET active=0 WHERE id=@PlanId AND application_id=@AppId AND active=1",
                new { PlanId = planId, AppId = appId }) > 0;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    private static string SerializePricingOptions(IReadOnlyList<PlanOptionRequest>? options)
    {
        var source = options ?? [];
        var nextId = source.Where(option => option.Id is > 0).Select(option => option.Id!.Value).DefaultIfEmpty(0).Max() + 1;
        var seenIds = new HashSet<int>();
        var normalized = new List<PlanOptionRequest>(source.Count);
        foreach (var option in source)
        {
            var id = option.Id is > 0 && seenIds.Add(option.Id.Value)
                ? option.Id.Value
                : nextId++;
            seenIds.Add(id);
            var durationSeconds = option.DurationSeconds > 0
                ? option.DurationSeconds
                : option.DurationDays * 86400;
            normalized.Add(option with
            {
                Id = id,
                Currency = option.Currency.ToUpperInvariant(),
                DurationSeconds = durationSeconds,
                DurationDays = durationSeconds / 86400
            });
        }
        return JsonSerializer.Serialize(normalized, PricingJsonOptions);
    }

    public async Task<object> CreateCustomerAsync(Guid appId, CustomerRequest request)
    {
        try
        {
            var id = Guid.NewGuid();
            await using var db = await connections.OpenConnectionAsync();
            await db.ExecuteAsync(
                "INSERT INTO customers(id,application_id,external_id,email,created_utc) VALUES(@id,@appId,@ExternalId,@Email,UTC_TIMESTAMP(6)) ON DUPLICATE KEY UPDATE email=COALESCE(VALUES(email),email)",
                new { id, appId, request.ExternalId, request.Email });
            var customer = await db.QuerySingleAsync(
                "SELECT id,external_id ExternalId,email FROM customers WHERE application_id=@appId AND external_id=@ExternalId",
                new { appId, request.ExternalId });
            Logger.Debug(this.MethodName() + " | Customer row loaded after save. ApplicationId: {0}, ExternalId: {1}", appId, request.ExternalId);
            return customer;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<IEnumerable<object>> ListCustomersAsync(Guid appId)
    {
        try
        {
            Logger.Debug(this.MethodName() + " | Loading customers. ApplicationId: {0}", appId);
            await using var db = await connections.OpenConnectionAsync();
            var customers = (await db.QueryAsync(
                "SELECT id,external_id ExternalId,email,created_utc CreatedUtc FROM customers WHERE application_id=@appId",
                new { appId })).AsList();
            Logger.Debug(this.MethodName() + " | Customers loaded. ApplicationId: {0}, Count: {1}", appId, customers.Count);
            return customers;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }
}
