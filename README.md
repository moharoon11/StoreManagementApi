# Store Management solution

This folder is the workspace root. Open `StoreManagement.sln` in Visual Studio or open this folder in VS Code.

## Projects

- `StoreManagement.Api/` is the main ASP.NET Core API. Its `Program.cs`, `appsettings.json`, controllers, services, and other application files live here.
- `SubscriptionPlatform.Core/` is a separate class library containing the shared subscription and payment implementation. The API references it directly as a project dependency; it does not call another service over HTTP.
- `SubscriptionPlatform.Core/Migrations/` contains the subscription database SQL migrations and setup guide.

For a new StoreManagement installation, apply migrations `001_initial.sql` and `003_store_management_seed.sql` to the subscription database. Apply `002_fixed_term_prices.sql` only if upgrading an older schema that is missing the fixed-term price columns. Migration 003 seeds the application and sample plan IDs already configured in `StoreManagement.Api/appsettings.json`; the sample prices are for testing and must be changed before live payments.

Both projects are listed in `StoreManagement.sln`. Keep the project folders together when copying or publishing the source workspace so the project reference remains available. Build or publish the main API from the solution root:

```sh
dotnet build StoreManagement.sln
dotnet publish StoreManagement.Api/StoreManagement.Api.csproj -c Release
```

The published API output includes the class library assembly. The server only needs that publish output and the configured runtime secrets and database connection values; it does not need the source project folders.

## Logs

The API loads `nlog.config` from its published output and writes application logs to `Logs/Applog.*.log` and subscription logs to `Logs/SubscriptionLog.*.log` beside the executable. NLog messages in controllers, services, and repositories record important operations and failures in plain text. Controller actions catch exceptions and return the existing API error response format, with a request ID in the log. The email worker records delivery failures. ASP.NET Core's own framework logs continue to use the default console logger, so the host should retain console output as well. The files are archived daily and retained for 14 days by `nlog.config`.

For the end-to-end payment, Razorpay dashboard, database, deployment, and Flutter release steps, open [PAYMENT_GO_LIVE_GUIDE.html](PAYMENT_GO_LIVE_GUIDE.html) in a browser.

For a slower first-time walkthrough with example commands and expected results, open [PAYMENT_GO_LIVE_FIRST_TIME.html](PAYMENT_GO_LIVE_FIRST_TIME.html) in a browser.

For the same first-time walkthrough in Tamil, open [PAYMENT_GO_LIVE_FIRST_TIME_TAMIL.html](PAYMENT_GO_LIVE_FIRST_TIME_TAMIL.html) in a browser.
