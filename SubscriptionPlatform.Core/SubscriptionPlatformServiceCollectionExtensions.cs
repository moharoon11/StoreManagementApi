using Dapper;
using Microsoft.Extensions.DependencyInjection;
using SubscriptionPlatform.Api.Data;
using SubscriptionPlatform.Api.Repositories;
using SubscriptionPlatform.Api.Services;

namespace SubscriptionPlatform.Core;

public static class SubscriptionPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddSubscriptionPlatformCore(this IServiceCollection services)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        services.AddSingleton<ConnectionFactory>();
        services.AddScoped<RazorpayService>();
        services.AddScoped<CatalogRepository>();
        services.AddScoped<SubscriptionRepository>();
        services.AddScoped<PaymentRepository>();
        services.AddScoped<CatalogService>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<StoreSubscriptionService>();
        services.AddHostedService<SubscriptionMaintenanceWorker>();
        return services;
    }
}
