namespace StoreManagement.Api.Services;

public interface IGeoLocationService
{
    Task<(string? City, string? Region)> LookupAsync(string? publicIp, CancellationToken cancellationToken = default);
}
