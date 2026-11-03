using System.Net;
using System.Net.Http.Json;
using NLog;

namespace StoreManagement.Api.Services;

public sealed class GeoLocationService : IGeoLocationService
{
    private static readonly Logger Logger = LogManager.GetLogger("GeoLocationService");
    private readonly IHttpClientFactory _clientFactory;
    private readonly IConfiguration _configuration;
    public GeoLocationService(IHttpClientFactory clientFactory, IConfiguration configuration) => (_clientFactory, _configuration) = (clientFactory, configuration);
    public async Task<(string? City, string? Region)> LookupAsync(string? publicIp, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicIp) || !IPAddress.TryParse(publicIp, out var ip) || IPAddress.IsLoopback(ip))
        {
            Logger.Debug(this.MethodName() + " | Geo location lookup skipped: public IP is missing or invalid.");
            return (null, null);
        }
        var template = _configuration["GeoIp:LookupUrl"];
        if (string.IsNullOrWhiteSpace(template))
        {
            Logger.Debug(this.MethodName() + " | Geo location lookup skipped: lookup URL is not configured.");
            return (null, null);
        }
        try
        {
            var client = _clientFactory.CreateClient(nameof(GeoLocationService));
            client.Timeout = TimeSpan.FromSeconds(2);
            var url = template.Replace("{ip}", Uri.EscapeDataString(publicIp));
            var data = await client.GetFromJsonAsync<GeoIpResponse>(url, cancellationToken);
            Logger.Debug(this.MethodName() + " | Geo location lookup completed. CityFound: {0}, RegionFound: {1}",
                !string.IsNullOrWhiteSpace(data?.City), !string.IsNullOrWhiteSpace(data?.Region ?? data?.RegionName));
            return (data?.City, data?.Region ?? data?.RegionName);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"{this.MethodName()} | Geo location lookup failed; returning an empty location. Exception: {ex.Message}");
            return (null, null); // Geolocation is best-effort and must never block recovery.
        }
    }
    private sealed class GeoIpResponse { public string? City { get; set; } public string? Region { get; set; } public string? RegionName { get; set; } }
}
