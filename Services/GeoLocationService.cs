using System.Net;
using System.Net.Http.Json;

namespace StoreManagement.Api.Services;

public sealed class GeoLocationService : IGeoLocationService
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly IConfiguration _configuration;
    public GeoLocationService(IHttpClientFactory clientFactory, IConfiguration configuration) => (_clientFactory, _configuration) = (clientFactory, configuration);
    public async Task<(string? City, string? Region)> LookupAsync(string? publicIp, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicIp) || !IPAddress.TryParse(publicIp, out var ip) || IPAddress.IsLoopback(ip)) return (null, null);
        var template = _configuration["GeoIp:LookupUrl"];
        if (string.IsNullOrWhiteSpace(template)) return (null, null);
        try
        {
            var client = _clientFactory.CreateClient(nameof(GeoLocationService));
            client.Timeout = TimeSpan.FromSeconds(2);
            var url = template.Replace("{ip}", Uri.EscapeDataString(publicIp));
            var data = await client.GetFromJsonAsync<GeoIpResponse>(url, cancellationToken);
            return (data?.City, data?.Region ?? data?.RegionName);
        }
        catch { return (null, null); } // Geolocation is best-effort and must never block recovery.
    }
    private sealed class GeoIpResponse { public string? City { get; set; } public string? Region { get; set; } public string? RegionName { get; set; } }
}
