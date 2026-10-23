# Store Management solution

This folder is the workspace root. Open `StoreManagement.sln` in Visual Studio or open this folder in VS Code.

## Projects

- `StoreManagement.Api/` is the main ASP.NET Core API. Its `Program.cs`, `appsettings.json`, controllers, services, and other application files live here.
- `SubscriptionPlatform.Core/` is a separate class library containing the shared subscription and payment implementation. The API references it directly as a project dependency; it does not call another service over HTTP.

Both projects are listed in `StoreManagement.sln`. Keep the project folders together when copying or publishing the source workspace so the project reference remains available. Build or publish the main API from the solution root:

```sh
dotnet build StoreManagement.sln
dotnet publish StoreManagement.Api/StoreManagement.Api.csproj -c Release
```

The published API output includes the class library assembly. The server only needs that publish output and the configured runtime secrets and database connection values; it does not need the source project folders.
