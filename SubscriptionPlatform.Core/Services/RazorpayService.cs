using System.Security.Cryptography;
using System.Text;
using NLog;
using Razorpay.Api;

namespace SubscriptionPlatform.Api.Services;

public sealed class RazorpayService(IConfiguration config)
{
    private static readonly Logger Logger = LogManager.GetLogger("SubscriptionPlatform.Services.RazorpayService");
    public sealed record ProviderPayment(string Id, string OrderId, string Status, long Amount, string Currency);
    public bool Configured =>
        !string.IsNullOrWhiteSpace(config["Razorpay:KeyId"])
        && !string.IsNullOrWhiteSpace(config["Razorpay:KeySecret"]);

    public string KeyId
    {
        get { return config["Razorpay:KeyId"] ?? ""; }
    }
    public string CreateOrder(long amount, string currency, string receipt)
    {
        try
        {
            if (!Configured)
            {
                Logger.Error(this.MethodName() + " | Razorpay credentials are not configured.");
                throw new InvalidOperationException("Razorpay is not configured. Set Razorpay:KeyId and Razorpay:KeySecret.");
            }
            Logger.Debug(this.MethodName() + " | Creating provider order. AmountMinor: {0}, Currency: {1}", amount, currency);
            var client = new RazorpayClient(KeyId, config["Razorpay:KeySecret"]);
            var result = client.Order.Create(new Dictionary<string, object>
            {
                ["amount"] = amount,
                ["currency"] = currency,
                ["receipt"] = receipt
            });
            var orderId = result["id"].ToString()!;
            Logger.Info(this.MethodName() + " | Provider order created. OrderId: {0}", orderId);
            return orderId;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Service operation failed.");
            throw;
        }
    }
    public ProviderPayment FetchPayment(string paymentId)
    {
        try
        {
            if (!Configured)
            {
                Logger.Error(this.MethodName() + " | Razorpay credentials are not configured.");
                throw new InvalidOperationException("Razorpay is not configured.");
            }
            Logger.Debug(this.MethodName() + " | Fetching provider payment. PaymentId: {0}", paymentId);
            var client = new RazorpayClient(KeyId, config["Razorpay:KeySecret"]);
            var payment = client.Payment.Fetch(paymentId);
            var result = new ProviderPayment(payment["id"].ToString()!, payment["order_id"].ToString()!, payment["status"].ToString()!, Convert.ToInt64(payment["amount"]), payment["currency"].ToString()!);
            Logger.Info(this.MethodName() + " | Provider payment fetched. PaymentId: {0}, Status: {1}", paymentId, result.Status);
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Service operation failed.");
            throw;
        }
    }
    public bool VerifyPayment(string orderId, string paymentId, string signature)
    {
        try
        {
            var verified = Verify($"{orderId}|{paymentId}", signature, config["Razorpay:KeySecret"]);
            Logger.Debug(this.MethodName() + " | Provider payment signature valid: {0}. OrderId: {1}, PaymentId: {2}", verified, orderId, paymentId);
            return verified;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Service operation failed.");
            throw;
        }
    }

    public bool VerifyWebhook(string payload, string signature)
    {
        try
        {
            var verified = Verify(payload, signature, config["Razorpay:WebhookSecret"]);
            Logger.Debug(this.MethodName() + " | Provider webhook signature valid: {0}. PayloadLength: {1}", verified, payload.Length);
            return verified;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Service operation failed.");
            throw;
        }
    }
    private static bool Verify(string message, string? signature, string? secret)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(secret)) return false;
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(message)));
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(signature));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
