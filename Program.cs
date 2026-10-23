using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MySqlConnector;
using NLog.Web;
using StoreManagement.Api.Data;
using StoreManagement.Api.Filters;
using StoreManagement.Api.Middleware;
using StoreManagement.Api.Repositories;
using StoreManagement.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Logging.ClearProviders();
builder.Host.UseNLog();

// 1. Database Connection Pooling via MySqlDataSource
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be set through the host secret configuration.");

var dataSource = new MySqlDataSource(connectionString);
builder.Services.AddSingleton(dataSource);
builder.Services.AddScoped<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddScoped<ControllerLoggingFilter>();

// 2. Services Registration
builder.Services.AddHttpClient();
builder.Services.AddScoped<SubscriptionPlatformClient>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddSingleton<EmailQueueService>();
builder.Services.AddSingleton<IEmailQueue>(sp => sp.GetRequiredService<EmailQueueService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<EmailQueueService>());
builder.Services.AddScoped<IGeoLocationService, GeoLocationService>();
builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();
builder.Services.AddScoped<IPdfService, PdfService>();
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

builder.Services.AddControllers(options => options.Filters.AddService<ControllerLoggingFilter>());
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
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment() || true) // Enable Swagger in all environments for testing ease
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Store Management API v1");
        c.RoutePrefix = string.Empty; // Serve Swagger UI at root URL (http://localhost:5000/)
    });
}

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseMiddleware<SubscriptionAccessMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.Run();
