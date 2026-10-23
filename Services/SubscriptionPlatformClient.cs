using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace StoreManagement.Api.Services;

public sealed class SubscriptionPlatformClient(IHttpClientFactory factory, IConfiguration configuration, ILogger<SubscriptionPlatformClient> logger)
{
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTime _tokenExpiresUtc;

    private string BaseUrl => (configuration["SubscriptionPlatform:BaseUrl"] ?? "").TrimEnd('/');

    private async Task<HttpClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(configuration["SubscriptionPlatform:ApplicationKey"]))
            throw new InvalidOperationException("SubscriptionPlatform BaseUrl and ApplicationKey must be configured on the Store API server.");
        if (_token is null || _tokenExpiresUtc <= DateTime.UtcNow.AddMinutes(2))
        {
            await _tokenLock.WaitAsync(cancellationToken);
            try
            {
                if (_token is null || _tokenExpiresUtc <= DateTime.UtcNow.AddMinutes(2))
                {
                    var auth = factory.CreateClient();
                    var response = await auth.PostAsJsonAsync($"{BaseUrl}/api/v1/auth/token", new { applicationKey = configuration["SubscriptionPlatform:ApplicationKey"] }, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    _token = json.RootElement.GetProperty("access_token").GetString()!;
                    _tokenExpiresUtc = DateTime.UtcNow.AddSeconds(json.RootElement.GetProperty("expires_in").GetInt32());
                }
            }
            finally { _tokenLock.Release(); }
        }
        var client = factory.CreateClient();
        client.BaseAddress = new Uri(BaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return client;
    }

    public async Task<JsonDocument> GetPlansAsync(CancellationToken ct)
    {
        using var client = await CreateClientAsync(ct);
        var response = await client.GetAsync("/api/v1/plans", ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<JsonDocument> GetStatusAsync(int userId, CancellationToken ct)
    {
        using var client = await CreateClientAsync(ct);
        var response = await client.GetAsync($"/api/v1/subscriptions/by-customer/{userId}", ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<JsonDocument> StartAsync(int userId, string period, bool trial, string idempotencyKey, CancellationToken ct)
    {
        using var client = await CreateClientAsync(ct);
        var customerResponse = await client.PostAsJsonAsync("/api/v1/customers", new { externalId = userId.ToString() }, ct);
        customerResponse.EnsureSuccessStatusCode();
        using var customer = JsonDocument.Parse(await customerResponse.Content.ReadAsStringAsync(ct));
        var customerId = customer.RootElement.GetProperty("id").GetGuid();
        var plansResponse = await client.GetAsync("/api/v1/plans", ct);
        plansResponse.EnsureSuccessStatusCode();
        using var plans = JsonDocument.Parse(await plansResponse.Content.ReadAsStringAsync(ct));
        var configuredId = Guid.Parse(configuration["SubscriptionPlatform:PlanId"] ?? throw new InvalidOperationException("SubscriptionPlatform:PlanId is required."));
        var plan = plans.RootElement.EnumerateArray().FirstOrDefault(p => p.GetProperty("id").GetGuid() == configuredId);
        if (plan.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("Configured subscription plan was not found for this integration.");
        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new { customerId, planId = configuredId, billingPeriod = period, idempotencyKey, startTrial = trial }, ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<bool> VerifyAsync(Guid paymentId, string orderId, string providerPaymentId, string signature, CancellationToken ct)
    {
        using var client = await CreateClientAsync(ct);
        var response = await client.PostAsJsonAsync("/api/v1/payments/verify", new { paymentId, orderId, razorpayPaymentId = providerPaymentId, signature }, ct);
        if (!response.IsSuccessStatusCode) logger.LogWarning("Payment verification rejected by subscription platform for {PaymentId}", paymentId);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> MarkFailedAsync(int userId, Guid paymentId, string orderId, CancellationToken ct)
    {
        using var client = await CreateClientAsync(ct);
        var response = await client.PostAsJsonAsync("/api/v1/payments/failure", new { paymentId, orderId, externalCustomerId = userId.ToString() }, ct);
        return response.IsSuccessStatusCode;
    }
}
