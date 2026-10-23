# SubscriptionPlatform: Integration and Live Deployment Guide

This guide explains the purpose and current behavior of this API, how another application calls it, and what must be finished before accepting real payments. It is written for application developers and the team operating the API.

> **Payment model:** fixed terms are prepaid one-time Razorpay orders, not automatic recurring mandates. The callback and captured-webhook paths now verify Razorpay's server-side payment state and reconcile the stored order, amount, and currency before activating access. Run the complete test-mode acceptance and deployment checklist below before using live credentials. Recurring collection, refunds, invoices, automated integration tests, customer-facing account/billing management, and application-key rotation remain separate production features and are not included in this fixed-term checkout.

## 1. What this project is for

SubscriptionPlatform is a server-side REST API that gives multiple registered applications a common place to create their own plans, customers, and subscriptions. The API is built with ASP.NET Core MVC controllers, a controller → service → repository structure, Dapper, and MySQL. Razorpay's .NET SDK is used to create payment orders. Payment signature and webhook signature checks are performed by the server.

An integration can use the API to:

- Register an application identity and receive an application key.
- Create and list application-owned plans with 3-month, 6-month, and 1-year prices, feature JSON, limits JSON, and trial duration.
- Create and list application-owned customer records.
- Start a free trial or create a pending paid subscription and Razorpay order.
- Submit checkout results for server-side signature verification.
- Read subscription/payment records, request end-of-period cancellation, and change a plan.
- Receive Razorpay webhooks for captured or failed payments.

There is no frontend in this project. Each integrating application supplies its own customer-facing UI and calls this API from its trusted backend. Never put the application key, admin key, JWT signing key, Razorpay key secret, or webhook secret in a browser or mobile binary.

### Important multi-application / merchant boundary

Application data is separated by `application_id`, so each integration sees its own plans, customers, and subscriptions. However, the Razorpay key ID, key secret, and webhook secret are currently global API configuration. As implemented, all applications create orders under the same configured Razorpay merchant account. This is appropriate only when the API operator is the merchant of record and receives the funds. It does **not** let each customer application connect its own Razorpay merchant account. A marketplace or per-tenant merchant model needs a separately designed and reviewed provider-account model (for example, an approved Razorpay platform/Route integration), credential isolation, onboarding, reconciliation, and payout logic.

## 2. Project layout

| Folder/file | Responsibility |
|---|---|
| `Controllers/` | MVC route definitions, auth policies, request/response mapping, HTTP status codes. One controller per file. |
| `Services/` | Business rules, Razorpay operations, background expiry work, health checks, and exception logging. |
| `Repositories/` | Dapper SQL, tenant-scoped queries, and database transaction boundaries. |
| `../` | The `SubscriptionPlatform.Core` class library project, containing the shared Dapper repositories, subscription/payment services, Razorpay integration, maintenance worker, and this `Migrations/` folder. The main StoreManagement API references this project directly. |
| `Models/Requests.cs` | Request and response contracts used by controller/service methods. |
| `Data/ConnectionFactory.cs` | MySQL connection creation from configuration. |
| `Migrations/001_initial.sql` | Initial MySQL schema: database, tables, keys, indexes, and foreign keys. |
| `Startup.cs`, `Program.cs` | Conventional ASP.NET Core host, MVC, auth, DI, OpenAPI, middleware, and service registration. |

All stored timestamps are intended to be UTC. Amount fields use integer minor units (for example, paise for INR); clients must not send floating-point amounts.

## 3. Set up a development or staging environment

### Requirements

- .NET SDK 10
- MySQL 8 or a compatible MySQL service
- A Razorpay account with **test mode** API keys for checkout testing
- A public HTTPS staging URL or secure development tunnel to receive Razorpay webhooks

### Database setup

The SQL migration is in `StoreManagementApi/SubscriptionPlatform.Core/Migrations/`. From the StoreManagement solution root, apply it with a privileged migration account:

```bash
mysql -u root -p < SubscriptionPlatform.Core/Migrations/001_initial.sql
```

Then create a restricted runtime account. Apply least privilege for the database host and operational requirements; the API runtime needs data access, not schema-owner privileges:

```sql
CREATE USER 'subscription_api'@'10.%' IDENTIFIED BY 'replace-with-a-unique-long-password';
GRANT SELECT, INSERT, UPDATE, DELETE ON SubscriptionDatabase.* TO 'subscription_api'@'10.%';
FLUSH PRIVILEGES;
```

Restrict the host pattern to the API's actual network. Do not expose MySQL publicly. Store database backups away from the database host and test restoring them.

### Configure secrets

`appsettings.json` contains placeholders only. Startup deliberately rejects the placeholder DB password and auth secrets. Configure secrets through environment variables locally or through the deployment platform's secret manager in staging/production:

```bash
export ConnectionStrings__SubscriptionDatabase='Server=db-host;Port=3306;Database=SubscriptionDatabase;User ID=subscription_api;Password=...;GuidFormat=Char36;SslMode=Required;'
export Authentication__SigningKey="$(openssl rand -base64 48)"
export Authentication__BootstrapAdminKey="$(openssl rand -hex 32)"
export Razorpay__KeyId='rzp_test_...'
export Razorpay__KeySecret='...'
export Razorpay__WebhookSecret='a-separate-random-test-webhook-secret'
```

The JWT signing key and bootstrap admin key are separate secrets. Losing or replacing the JWT signing key invalidates all issued bearer tokens. The current API has no application-key rotation endpoint; if an application key is exposed, deactivate that integration and register a replacement, then update the integration. Existing JWTs remain valid until they expire (up to one hour), so plan for that window.

### Build and run

For local subscription administration, run these commands from the `StoreManagementApi` solution root. The standalone host is a provisioning tool; StoreManagement API uses the Core class library directly at runtime.

```bash
dotnet build StoreManagement.sln --configuration Release
dotnet run --project ../SubscriptionPlatform.Api/SubscriptionPlatform.Api.csproj
```

The launch profile is for local development. Production should run behind a TLS-terminating load balancer or reverse proxy with forwarded-header configuration reviewed for the hosting topology. The API must be reachable by Razorpay at the webhook URL over HTTPS.

OpenAPI JSON is available at `/openapi/v1.json`. `GET /health/live` indicates that the process responds. `GET /health/ready` runs the configured MySQL check and returns `503` when the database is unavailable.

## 4. Authentication and tenant isolation

There are two implemented bearer-token roles:

- **Platform admin**: authenticates with `Authentication:BootstrapAdminKey`; may register, list, and deactivate integrations.
- **Application integration**: authenticates with the one-time application key returned at registration; its JWT contains `app_id`, which repository/service operations use to scope business data.

The `Customer` authorization policy exists in configuration, but there is no customer login/token issuance flow or customer-authorized controller in this version. Integrating applications must keep their own customer authentication and call the API from their server. Do not give application bearer tokens to end users.

Bearer tokens are signed JWTs with the configured issuer/audience and one-hour lifetime. Pass them in `Authorization: Bearer <token>`. The public token endpoint itself requires a valid bootstrap/application credential in its JSON body. The application key is a service credential; use TLS and keep it in a server-side secret store.

## 5. Endpoint reference

All business routes are under `/api/v1`. JSON uses camelCase on the wire. Every example below is a request shape, not a secret to reuse.

### `POST /api/v1/auth/token` — obtain an access token

Public route with credential validation. Exchange one of these bodies for a bearer token:

```json
{ "adminKey": "<bootstrap-admin-key>" }
```

or:

```json
{ "applicationKey": "<application-key>" }
```

Returns `200` with `access_token`, `token_type`, and `expires_in`. Invalid credentials return `401`. Cache the token server-side until shortly before expiry; do not request a token for every API operation.

### `POST /api/v1/admin/applications` — register an integrating application

Requires a platform-admin bearer token. Body:

```json
{ "name": "Storefront A" }
```

Returns `201` with the new application ID and `application_key`. Save the key immediately: the API stores only its hash and does not return it again. Invalid/empty names return `400`.

### `GET /api/v1/admin/applications` — list integrations

Requires platform-admin authorization. Returns application IDs, names, active state, and creation time. It never returns application keys or key hashes.

### `POST /api/v1/admin/applications/{id}/deactivate` — disable an integration

Requires platform-admin authorization. Returns `204` if an application was deactivated or `404` if the ID does not exist. Deactivation prevents new tokens from being issued, but already-issued JWTs are not revoked immediately.

### `POST /api/v1/plans` — create a plan

Requires an application bearer token. Body example:

```json
{
  "name": "Pro",
  "currency": "INR",
  "monthlyAmountMinor": 49900,
  "yearlyAmountMinor": 499000,
  "trialDays": 7,
  "featuresJson": "{\"reports\":true}",
  "limitsJson": "{\"seats\":10}"
}
```

Prices are integer minor units (here, `49900` means ₹499.00). `trialDays` must be between 0 and 90. A plan is owned by the caller's application and is created active. Returns `201`; invalid fields return `400`; inactive application credentials are forbidden. `featuresJson` and `limitsJson` are stored, but this version does not enforce feature entitlements or usage limits.

### `GET /api/v1/plans` — list active plans

Requires an application bearer token. Returns active plans for that application only, with prices, currency, trial days, and feature/limit JSON.

### `POST /api/v1/customers` — create or find a customer

Requires an application bearer token. Body:

```json
{ "externalId": "customer-392", "email": "person@example.com" }
```

`externalId` is the integrating application's stable customer ID. The database enforces uniqueness for `(application_id, external_id)`. Repeating the request returns the existing customer record. Invalid external IDs or email addresses return `400`.

### `GET /api/v1/customers` — list customers

Requires an application bearer token. Returns only customers registered under that application's tenant ID.

### `POST /api/v1/subscriptions` — start trial or create a paid order

Requires an application bearer token. Body example for a paid subscription:

```json
{
  "customerId": "<customer-guid>",
  "planId": "<plan-guid>",
  "billingPeriod": "monthly",
  "idempotencyKey": "storefront-a-checkout-01J...",
  "startTrial": false
}
```

`billingPeriod` is `monthly` or `yearly`. `idempotencyKey` must be unique across the whole database, not just within a tenant; use a random UUID or an application-prefixed unique value. If eligible and `startTrial` is true, the API creates a `trialing` subscription. Otherwise it creates a `pending_payment` subscription and payment row, then asks Razorpay to create an order. A paid response contains `subscriptionId`, `paymentId`, `orderId`, Razorpay `keyId`, `amountMinor`, and `currency`; use the returned server-created order values in checkout rather than calculating them in the client. Missing tenant customer/plan returns `404`; invalid input returns `400`; an existing current subscription or ineligible trial returns `409`.

Current limitations: only one current subscription is allowed per customer; a trial is blocked if the customer has any subscription history. An idempotency key that already exists returns the previous payment/subscription state. This API does not automatically charge a renewal when a billing period ends. Yearly/monthly currently define the local period duration and order amount; they do not create a Razorpay recurring mandate.

### `GET /api/v1/subscriptions/{id}` — read a subscription

Requires an application bearer token. Returns the subscription and plan name only if it belongs to the caller's application; otherwise returns `404`.

### `POST /api/v1/subscriptions/{id}/cancel` — mark period-end cancellation

Requires an application bearer token. Returns `200` with `cancelAtPeriodEnd=true` for an active/trial subscription; otherwise returns `409`. This currently sets a database flag only. It does not cancel a Razorpay subscription or stop a renewal because automatic provider renewals are not implemented.

### `POST /api/v1/subscriptions/{id}/change-plan?planId={guid}` — change the plan

Requires an application bearer token. Both subscription and new plan must belong to the caller's application. Returns `200` with the chosen IDs, or `409` when the subscription is not active/trialing or the target plan is not eligible. This implementation changes the plan immediately and does not calculate a price difference, charge, proration, or invoice. Do not use it as a customer-facing billing change until that policy is implemented.

### `POST /api/v1/payments/verify` — verify a checkout callback

Requires an application bearer token. The integrating server forwards Razorpay's checkout callback values:

```json
{
  "paymentId": "<payment-guid-returned-by-this-api>",
  "orderId": "order_...",
  "razorpayPaymentId": "pay_...",
  "signature": "<razorpay_signature>"
}
```

The service computes HMAC-SHA256 for `order_id|payment_id` using the configured Razorpay key secret and constant-time compares it to the signature. It fetches the payment from Razorpay, requires `captured`, and compares provider order ID, payment ID, amount, and currency against the stored payment/order before activation. A bad signature, mismatched provider data, unknown payment, or payment not yet captured is rejected. Razorpay requires server-side signature verification and recommends fulfilment only after the required capture state is confirmed; see the [Razorpay integration guidance](https://razorpay.com/docs/server-integration/python/test-app/) and [security checklist](https://security.razorpay.com/security/checklist/).

### `POST /api/v1/payments/failure` — release a failed checkout attempt

Requires an application bearer token. The trusted integrating backend sends `{ "paymentId": "<platform-payment-guid>", "orderId": "order_...", "externalCustomerId": "<its-own-user-id>" }` after the checkout SDK reports failure. The API verifies application ownership, external customer ownership, and the exact stored order, then closes the failed pending term so the customer may retry. It never changes a paid payment. Returns `200` if recorded or `409` when the payment is no longer pending/matching. An order abandoned without a callback expires after 30 minutes.

### `GET /api/v1/payments` — list payment history

Requires an application bearer token. Returns payment rows joined to subscriptions belonging to that application. It currently exposes payment ID, subscription ID, amount, currency, state, and timestamps; it is not an invoice endpoint.

### `POST /api/v1/webhooks/razorpay` — receive payment events

No bearer token. Razorpay must send `X-Razorpay-Signature` and `X-Razorpay-Event-Id`. The service verifies the HMAC over the raw request body with `Razorpay:WebhookSecret`, then records the event ID under a unique database constraint. Duplicate event IDs return `200` with `duplicate=true`. For `payment.captured`, it fetches the provider payment and checks captured state, order ID, amount, and currency before activating. A signed `payment.failed` event closes the pending one-time term without granting grace access, so the customer can retry. Unknown event types are acknowledged and recorded. Invalid signatures/provider mismatches return `401`; a missing event ID returns `400`.

Webhook event IDs are persisted for duplicate suppression, but there is no durable retry queue for transient database/provider failures and no comprehensive reconciliation job for out-of-order event histories. Razorpay describes webhooks as asynchronous signals and recommends verifying state for critical flows; see [Razorpay payment status and webhook guidance](https://razorpay.com/docs/server-integration/python/test-app/) and [payment event behavior](https://razorpay.com/docs/payments/payments/dashboard/?preferred-country=IN).

### `GET /health/live` and `GET /health/ready` — platform probes

Public MVC actions. `/live` confirms that the web process responds. `/ready` invokes the MySQL health check; returns `200` when healthy, `503` otherwise. Configure the load balancer/orchestrator to use readiness for traffic admission and liveness for process restart decisions.

## 6. Integrate one application

The following is the intended server-to-server flow. Replace `https://api.example.com` with the deployed HTTPS base URL.

1. An operator obtains an admin token from `POST /api/v1/auth/token` and registers the integration with `POST /api/v1/admin/applications`.
2. Store the returned application key in the integrating application's backend secret store. The app backend requests an application JWT from `/api/v1/auth/token` and reuses it until near expiry.
3. Create a plan using `POST /api/v1/plans`; store the returned plan ID in the integrating application's own plan mapping. Create/find a customer using `POST /api/v1/customers`; store the returned customer ID in that mapping too.
4. When the customer starts a checkout, the integrating backend calls `POST /api/v1/subscriptions` with those IDs, the chosen billing period, and a new idempotency key.
5. If an order is returned, the integrating UI launches the Razorpay-supported checkout experience using the returned **Key ID**, `orderId`, `amountMinor`, and `currency`. The Key ID is intended for checkout configuration; the Key Secret stays only on this API server. Follow the [Razorpay integration documentation](https://razorpay.com/integrations/) for the current client SDK matching the integrating app's platform.
6. On checkout completion, the integrating backend forwards the callback identifiers/signature to `POST /api/v1/payments/verify`. Do not let a browser directly decide or persist subscription success. The callback should be treated as untrusted input until the API verifies it.
7. Configure the Razorpay webhook to call `/api/v1/webhooks/razorpay` as a second asynchronous notification path. Use test mode first; Razorpay supports test webhooks for staging (see [test webhook guidance](https://axisbank-docs.razorpay.com/webhooks/test/)).
8. The integrating application reads `GET /api/v1/subscriptions/{id}` to render current state. It should handle `pending_payment`, `trialing`, `active`, `grace`, `expired`, and other returned state strings without assuming a checkout callback always arrives.

Because there is no customer token/ownership flow, an application must authenticate its own end users and authorize each customer's access before its backend calls the subscription API. Never accept an arbitrary `customerId` from an untrusted client and forward it without checking that it belongs to the logged-in user.

## 7. Production deployment sequence

This is an operator checklist, not a claim that deployment has already been performed.

### Before creating production infrastructure

- Finish all items in “Required work before live” below. In particular, confirm payment captured state and amount/currency/order association with Razorpay before enabling entitlements or returning success.
- Decide merchant-of-record and settlement ownership. The current global Razorpay configuration means a single merchant account for every registered application.
- Complete a threat review for the API key/token bootstrap, multi-tenant isolation, idempotency, webhook retries/out-of-order events, refund reconciliation, and personal-data retention.
- Define support operations: payment disputes, failed webhooks, customer cancellation requests, refund authority, billing correction, and audit retention.

### Provision and deploy

1. Provision an HTTPS domain, application hosting with a supported .NET 10 runtime (or a controlled container image), a private MySQL 8 instance, network rules, TLS certificates, and a managed secret store. Keep MySQL private to the application network.
2. Apply `001_initial.sql` from a privileged deployment/migration identity. Do not give schema-admin credentials to the API runtime. Establish a repeatable migration history/release process before future schema changes; this initial SQL script is not an automated migration runner.
3. Create a restricted runtime DB account and configure the connection string with TLS enabled and `GuidFormat=Char36`.
4. Configure `Authentication:SigningKey`, `Authentication:BootstrapAdminKey`, Razorpay keys, and the DB password using the host's secret manager. Do not commit actual credentials or put them in a container image. Ensure test secrets and live secrets cannot be mixed between environments.
5. Publish/build the API in Release mode and deploy the output:

   ```bash
   dotnet restore SubscriptionPlatform.slnx
   dotnet publish SubscriptionPlatform.Api/SubscriptionPlatform.Api.csproj --configuration Release --output ./publish
   ```

   Use the hosting provider's managed process/container orchestration. Run multiple API instances only after verifying the hosted expiry worker's multi-instance behavior and making its work safely coordinated; currently each instance executes its own expiry loop.

6. Terminate TLS at a trusted load balancer/reverse proxy. Configure forwarded headers correctly for the chosen proxy and ensure direct untrusted requests cannot spoof forwarded IP/scheme values. Restrict inbound routes to intended HTTP(S) traffic; the webhook must be publicly reachable over HTTPS.
7. Configure probes: `/health/live` for process liveness and `/health/ready` for database readiness. Alert on repeated readiness failures, 5xx rate, Razorpay order errors, webhook failures/delay, and payment/subscription state mismatches.
8. Set up structured log aggregation with retention and access controls. Do not log credentials, signatures, full payment payloads, or unnecessary customer personal data. The global handler includes a trace ID in 500 responses; retain it in logs for incident investigation.
9. Run the full release and integration test suite against a disposable/staging database. At present this repository has no automated test project; add unit, repository integration, API integration, security, concurrency/idempotency, and webhook replay/out-of-order tests before production.
10. Test restore of database backups and practice rolling back the application release. Schema rollback needs a separately designed migration plan; do not assume the initial SQL file reverses schema changes.

### Razorpay test-to-live process

1. Complete Razorpay account onboarding and any activation checks that apply to the merchant account. Dashboard requirements vary by account and region.
2. Keep staging configured for test mode. Create test API credentials and a distinct test webhook secret. Configure the staging webhook for the exact public staging URL and the events used by the code (`payment.captured`, `payment.failed`). Confirm actual event IDs, signatures, retry delivery, and duplicate handling in tests.
3. Exercise successful checkout, decline/failure, abandoned checkout, delayed webhook, duplicate webhook, mismatched IDs, database interruption, API restart during payment processing, and expired subscription behavior. Confirm the database payment is actually captured before the application considers service active. Razorpay's guidance says test and live keys are separate and payment capture matters for fulfilment; see its [integration test and go-live steps](https://razorpay.com/docs/server-integration/python/test-app/) and [security checklist](https://security.razorpay.com/security/checklist/).
4. Only after code and staging sign-off, generate the merchant's live API keys in the live dashboard and store them in the production secret manager. Configure the production live webhook and its own webhook secret. Never reuse test keys or expose the key secret in the integrating application.
5. Start with a controlled rollout and monitor real payment status, capture, webhook deliveries, support issues, and settlement records. Razorpay's dashboard shows payment and settlement status; see [payment dashboard actions](https://razorpay.com/docs/payments/payments/dashboard/?preferred-country=IN).
6. Keep a documented rollback path. A payment provider key change is not a safe rollback if orders/payments already exist; preserve provider identifiers and reconcile outstanding orders before switching credentials.

## 8. Required work before live payments

The items below are real gaps in the checked-in implementation, not optional setup steps:

1. **Validate callback and webhook payment reconciliation in test mode.** Both paths fetch provider state before activation and compare order, amount, and currency. Exercise captured, delayed, duplicate, and mismatched events against the configured merchant account before launch.
2. **Make webhook transitions trustworthy and replay-safe.** Verify against raw request bytes, validate event shape, reconcile amount/currency/payment/order/account, handle capture versus authorization correctly, and persist processing state/errors for durable retries. Ensure out-of-order events cannot regress a final state. The current event table records deduplication but does not implement a durable retry queue or complete reconciliation.
3. **Implement actual recurring billing.** Schedule and create renewal charges/orders, retry failed renewals with idempotency, reconcile provider events, and support dunning/grace/cancellation transitions. The current worker only expires local periods; it does not charge renewals.
4. **Implement cancellation semantics.** Make `cancel_at_period_end` cause the correct status transition at the end date and ensure no future renewal is charged. Current cancellation is a flag and the worker expires periods without checking that flag.
5. **Implement refunds and invoices.** Add provider refund requests, idempotency, partial/full refund state reconciliation, refund webhook handling, invoice numbering/tax rules where applicable, and tenant-scoped admin/customer read routes. None are implemented here.
6. **Implement customer authorization and entitlements.** Add a secure customer auth/ownership design or make the integrating backend's server credential contract explicit, then enforce feature/limit data in a reliable usage ledger. JSON feature/limit values are stored only today.
7. **Implement credential rotation and revocation.** Add application key versioning/rotation, immediate token revocation or short-lived token checks, admin credential lifecycle/audit, and bootstrap admin rotation. Current app key has no rotate endpoint and already-issued JWTs remain valid until expiry.
8. **Add tests and migrations.** Add a test project for service rules, MySQL integration, multi-tenant isolation, concurrent trial/subscription creation, idempotency, payment callback/webhook signatures, duplicate/out-of-order events, and operational recovery. Establish numbered migration execution and migration history.
9. **Review financial/business rules.** Define taxes, refunds, price change/proration, billing timezone/calendar behavior, trial eligibility identity, concurrent app-user ownership, legal/privacy requirements, data deletion/retention, and currency support. Verify each rule with product/accounting/legal owners for the jurisdictions served.
10. **Run security and operational review.** Verify rate limits per client/IP/tenant, request size limits, HTTPS/proxy configuration, secret rotation, database backups/PITR, dependency vulnerability scans, least privilege, audit retention, monitoring/alerts, incident response, and disaster recovery.

Until these blockers are completed and verified, deploy only to local development or isolated staging using test credentials. A successful build alone is not proof of production readiness.

## 9. StoreManagement Flutter + Store API integration

This repository is the subscription backend. The sibling projects use it through the StoreManagement API, so the Flutter binary contains no Subscription Platform application key, Razorpay secret, or webhook secret.

### Available checkout terms

- **Free trial:** one trial per platform customer, using `trialDays` configured on the plan. This does not create a Razorpay order and does not automatically convert to a paid plan.
- **3 months:** prepaid one-time Razorpay order; access starts only after server verification and lasts three calendar months.
- **6 months:** prepaid one-time Razorpay order; access lasts six calendar months.
- **1 year:** prepaid one-time Razorpay order; access lasts twelve calendar months.

The fixed paid terms do not renew automatically. The customer must purchase another term after expiry. These are not Razorpay recurring subscriptions/mandates.

### Store API routes used by Flutter

All routes below are under the Store API `/api` base and require the Store API's normal user JWT.

| Route | Purpose |
|---|---|
| `GET /subscription/options` | Returns the configured plan and its trial duration, currency, and prices. Flutter displays only terms whose prices are greater than zero. |
| `GET /subscription/status` | Returns the logged-in Store user’s latest subscription status and access flag. |
| `POST /subscription/start` | Starts an eligible trial or creates a Razorpay order for `three_month`, `six_month`, or `one_year`. The Store API derives the customer from the signed-in user; clients cannot choose another customer or plan. |
| `POST /subscription/verify` | Sends Razorpay Checkout’s payment ID, order ID, and signature to the platform. Access remains locked unless the platform verifies the signature and confirms a captured payment with matching order, amount, and currency. |
| `POST /subscription/failure` | Records a failed Checkout attempt against the current Store user and order so the customer can try another term/order. It cannot mark a paid payment failed. |
| `POST /subscription/webhook/razorpay` | Public Razorpay webhook receiver hosted by StoreManagement API. It passes the signed event to the Core library for signature verification, provider reconciliation, and duplicate suppression. |

The Flutter app opens Razorpay Checkout with the Core library's returned Key ID, order ID, amount in minor units, and currency. A client success callback by itself does not unlock the application. Failed checkout callbacks are reported so the customer can retry; an order abandoned without a callback expires after 30 minutes. The Store API applies an access check to protected business routes and returns HTTP `402` when there is no valid trial or paid term; login, profile setup, health, and subscription routes remain available. Subscription database/provider errors fail closed for protected routes with HTTP `503`.

### Configure StoreManagement API

StoreManagement API references `SubscriptionPlatform.Core` as a project dependency and calls its `StoreSubscriptionService` in-process. There is no internal HTTP request, platform base URL, application key, or platform JWT involved at runtime. Configure:

```text
ConnectionStrings__SubscriptionDatabase=<MySQL connection string for SubscriptionDatabase>
SubscriptionPlatform__ApplicationId=<application-guid-created-in-the-standalone-platform-admin-api>
SubscriptionPlatform__PlanId=<plan-guid-created-for-this-integration>
Razorpay__KeyId=<Razorpay key ID>
Razorpay__KeySecret=<Razorpay key secret>
Razorpay__WebhookSecret=<Razorpay webhook secret>
ConnectionStrings__DefaultConnection=<StoreManagement database connection string>
JwtSettings__SecretKey=<random secret of at least 32 bytes>
```

All values belong in the Store API host's environment or secret manager, never Flutter. StoreManagement and the optional standalone SubscriptionPlatform API can point to the same `SubscriptionDatabase`; they use the same Core code. Do not run both application hosts concurrently unless you intentionally want both hosted maintenance workers running. The Store API can use its existing MySQL server with a separate `SubscriptionDatabase` schema. Razorpay credentials are process configuration in whichever host handles payments. The integration uses one merchant account for all plans/apps configured in that host; separate merchant accounts require a separate platform/Route design. Checked-in Store API database/JWT fallback credentials have been removed. Rotate credentials that were previously committed because deleting a value from the latest file does not erase it from Git history.

### Configure plan prices

Provision an application and plan using the optional standalone `SubscriptionPlatform.Api` admin host before running StoreManagement. Register the Store API integration and keep the returned application GUID. Use its application key to create one plan through `POST /api/v1/plans` on that standalone host. Example (replace prices with approved amounts; values are paise for INR):

```json
{
  "name": "BillMate",
  "currency": "INR",
  "monthlyAmountMinor": 0,
  "threeMonthAmountMinor": 149700,
  "sixMonthAmountMinor": 269700,
  "yearlyAmountMinor": 479400,
  "trialDays": 7,
  "featuresJson": "{}",
  "limitsJson": "{}"
}
```

Copy the returned application ID to `SubscriptionPlatform__ApplicationId`, and the plan's returned `id` to `SubscriptionPlatform__PlanId`. The application key is only needed for this one-time management setup (and future management API calls); StoreManagement does not use it at runtime. Enter actual commercial prices and supported currency; the sample values are illustrative only. Do not send customer-provided prices to create orders.

### Razorpay dashboard setup for real payments

1. Complete merchant onboarding, business verification, settlement bank setup, and required website/app details in the Razorpay Dashboard for the jurisdiction and account.
2. In **Test Mode**, generate test API Key ID and Key Secret. Configure them only in the Store API host's secret manager as `Razorpay__KeyId` and `Razorpay__KeySecret`.
3. Create a test webhook for `https://<store-api-host>/api/subscription/webhook/razorpay`. Subscribe to `payment.captured` and `payment.failed`, copy the webhook secret, and configure it as `Razorpay__WebhookSecret`. The webhook secret is separate from the API Key Secret. The Store API endpoint receives the event and the Core library verifies/reconciles it.
4. Apply the subscription schema to the configured SubscriptionDatabase, deploy the Store API to HTTPS staging, provision a test application/plan with the optional standalone API, configure the Store API environment values above, then install/build the Flutter app with that Store API base URL. No separate SubscriptionPlatform API host is required for payment runtime.
5. Test a successful captured test payment, failed/abandoned checkout, duplicate callback, duplicate webhook, delayed webhook, and platform outage. Confirm an unsuccessful or merely authorized payment never unlocks access.
6. After staging sign-off and Razorpay live activation, switch to **Live Mode**, generate live API credentials, and set the live key ID/key secret and live webhook secret in the Store API production secret manager. Create the production webhook at the production Store API URL with the same two events. Keep test and live credentials separate; never put either Key Secret in `--dart-define`, Dart source, the APK, or logs.
7. Make sure live payment capture is enabled or captured through the supported merchant flow. The current service grants access only to a Razorpay payment whose fetched status is `captured`.
8. Release the Flutter client configured to call the production Store API. Monitor Razorpay payment/webhook dashboards and Store API logs; reconcile settlements and payment records regularly.

Razorpay account activation requirements and Dashboard labels can vary. Use Razorpay's official current [test and go-live guidance](https://razorpay.com/docs/server-integration/python/test-app/) and [security checklist](https://security.razorpay.com/security/checklist/) when performing the dashboard steps. Never use real card details in test mode or test credentials for live payments.

### Database upgrade for an existing deployment

For a brand-new database, apply the current `001_initial.sql` only; it already creates both fixed-term price columns. Do not apply `002_fixed_term_prices.sql` after it. Migration 002 upgrades an older database that is missing those columns. It now checks before adding them, but you should still confirm the database version first. Record migration execution in your deployment system; the API does not run migrations automatically.

### Flutter build and payment boundary

The Flutter project depends on `razorpay_flutter` for Android/iOS native Checkout. Its Dart code receives only the public Key ID and server-created order details at runtime. Configure the existing Store API URL using the Flutter project's `API_BASE_URL` build define. Do not configure the Subscription Platform URL, application ID/key, database connection, or Razorpay secret in Flutter. This checkout package does not provide native web support; a future web client needs an appropriate hosted/web checkout implementation.
