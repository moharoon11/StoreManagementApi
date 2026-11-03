using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MySqlConnector;
using NLog;
using StoreManagement.Api.Controllers;
using StoreManagement.Api.Data;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Middleware;
using StoreManagement.Api.Repositories;
using StoreManagement.Api.Services;
using SubscriptionPlatform.Core;

var startupLogger = LogManager.GetLogger("Startup");
try
{
LogManager.Setup().LoadConfigurationFromFile(Path.Combine(AppContext.BaseDirectory, "nlog.config"));
startupLogger.Info("Main | API startup began.");
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// 1. Database Connection Pooling via MySqlDataSource
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be set through the host secret configuration.");
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("SubscriptionDatabase")))
    throw new InvalidOperationException("ConnectionStrings:SubscriptionDatabase must be set for the embedded subscription library.");
if (!Guid.TryParse(builder.Configuration["SubscriptionPlatform:ApplicationId"], out _) ||
    !Guid.TryParse(builder.Configuration["SubscriptionPlatform:PlanId"], out _))
    throw new InvalidOperationException("SubscriptionPlatform:ApplicationId and SubscriptionPlatform:PlanId must be configured as GUID values.");

builder.Services.AddSingleton<MySqlDataSource>(_ => new MySqlDataSource(connectionString));
builder.Services.AddScoped<IDbConnectionFactory, DbConnectionFactory>();

// 2. Services Registration
builder.Services.AddHttpClient();
builder.Services.AddSubscriptionPlatformCore();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddSingleton<EmailQueueService>();
builder.Services.AddSingleton<IEmailQueue>(sp => sp.GetRequiredService<EmailQueueService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<EmailQueueService>());
builder.Services.AddScoped<IGeoLocationService, GeoLocationService>();
builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();
builder.Services.AddScoped<IGeminiVisionService, GeminiVisionService>();

// 3. Repositories Registration
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IPasswordResetRepository, PasswordResetRepository>();
builder.Services.AddScoped<IStoreProfileRepository, StoreProfileRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IFavouriteRepository, FavouriteRepository>();
builder.Services.AddScoped<IBillingRepository, BillingRepository>();
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
builder.Services.AddScoped<ICustomerCreditInvoiceRepository, CustomerCreditInvoiceRepository>();
builder.Services.AddScoped<IStockRepository, StockRepository>();
builder.Services.AddScoped<IReportRepository, ReportRepository>();
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();

// 4. JWT Authentication Setup
var secretKey = builder.Configuration["JwtSettings:SecretKey"];
if (string.IsNullOrWhiteSpace(secretKey) || Encoding.UTF8.GetByteCount(secretKey) < 32)
    throw new InvalidOperationException("JwtSettings:SecretKey must be configured as a random secret of at least 32 bytes.");
var issuer = builder.Configuration["JwtSettings:Issuer"] ?? "StoreManagementApi";
var audience = builder.Configuration["JwtSettings:Audience"] ?? "StoreManagementFlutterApp";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// These per-IP quotas are launch defaults, not universal standards. Host configuration
// can override each PermitLimit and WindowSeconds after reviewing traffic and 429s.
builder.Services.AddRateLimiter(options =>
{
    var globalLimit = builder.Configuration.GetValue<int?>("RateLimiting:Global:PermitLimit") ?? 600;
    var globalWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Global:WindowSeconds") ?? 60;
    if (globalLimit < 1 || globalWindowSeconds < 1)
        throw new InvalidOperationException("RateLimiting:Global must have a positive PermitLimit and WindowSeconds.");

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = globalLimit,
                Window = TimeSpan.FromSeconds(globalWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    var authLimits = new[]
    {
        (Policy: nameof(AuthController.Register), Limit: 5, Window: TimeSpan.FromHours(1)),
        (Policy: nameof(AuthController.Login), Limit: 10, Window: TimeSpan.FromMinutes(1)),
        (Policy: nameof(AuthController.Refresh), Limit: 30, Window: TimeSpan.FromMinutes(1)),
        (Policy: nameof(AuthController.Logout), Limit: 30, Window: TimeSpan.FromMinutes(1)),
        (Policy: nameof(AuthController.ForgotPassword), Limit: 5, Window: TimeSpan.FromHours(1)),
        (Policy: nameof(AuthController.VerifyPasswordResetOtp), Limit: 10, Window: TimeSpan.FromMinutes(1)),
        (Policy: nameof(AuthController.ResetPassword), Limit: 10, Window: TimeSpan.FromMinutes(1))
    };

    foreach (var authLimit in authLimits)
    {
        var setting = $"RateLimiting:Auth:{authLimit.Policy}";
        var permitLimit = builder.Configuration.GetValue<int?>($"{setting}:PermitLimit") ?? authLimit.Limit;
        var windowSeconds = builder.Configuration.GetValue<int?>($"{setting}:WindowSeconds") ?? (int)authLimit.Window.TotalSeconds;
        if (permitLimit < 1 || windowSeconds < 1)
            throw new InvalidOperationException($"{setting} must have a positive PermitLimit and WindowSeconds.");

        options.AddPolicy(authLimit.Policy, context => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }
    options.OnRejected = async (rejection, cancellationToken) =>
    {
        var context = rejection.HttpContext;
        startupLogger.Warn("RateLimiter.OnRejected | Request limit reached. Path: {0}, RequestId: {1}",
            context.Request.Path, context.TraceIdentifier);
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        await context.Response.WriteAsJsonAsync(
            ApiResponse.ErrorResult("Too many requests. Please try again shortly."), cancellationToken);
    };
});

// 5. CORS Policy for Flutter Application
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddControllers();
var validationLogger = LogManager.GetLogger("ModelValidation");
builder.Services.PostConfigure<ApiBehaviorOptions>(options =>
{
    var existingResponseFactory = options.InvalidModelStateResponseFactory;
    options.InvalidModelStateResponseFactory = context =>
    {
        var invalidFields = string.Join(", ", context.ModelState
            .Where(field => field.Value?.Errors.Count > 0)
            .Select(field => field.Key));
        validationLogger.Debug(
            "InvalidModelStateResponseFactory | Request validation failed. Action: {0}, Fields: {1}, RequestId: {2}",
            context.ActionDescriptor.DisplayName, invalidFields, context.HttpContext.TraceIdentifier);
        return existingResponseFactory(context);
    };
});
builder.Services.AddEndpointsApiExplorer();

// 6. Swagger / OpenAPI Configuration with JWT Security Definition
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "Store Management & Billing API", 
        Version = "v1",
        Description = "REST API backend for Flutter Store Management, Billing, and Stock Management application."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer' followed by your valid JWT token.\nExample: Bearer eyJhbGciOiJIUzI1Ni..."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// 7. Middleware Pipeline

if (app.Environment.IsDevelopment() || true) // Enable Swagger in all environments for testing ease
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Store Management API v1");
        c.RoutePrefix = string.Empty; // Serve Swagger UI at root URL (http://localhost:5000/)
    });
}

app.UseRouting();
app.UseCors("AllowAll");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<SubscriptionAccessMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.Lifetime.ApplicationStarted.Register(() => startupLogger.Info("Main | API started | Environment: {0}", app.Environment.EnvironmentName));
app.Lifetime.ApplicationStopping.Register(() => startupLogger.Info("Main | API stopping."));
app.Run();
}
catch (Exception ex)
{
    startupLogger.Fatal(ex, "Main | API startup or host execution failed.");
    throw;
}
finally
{
    LogManager.Shutdown();
}
