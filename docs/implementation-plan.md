# Stocky — Implementation Plan

This plan turns the Portfolio Management feature spec into shippable milestones.
Stack: **.NET 10 Web API · React (Vite + TS) · Azure SQL · Microsoft Entra ID · Azure App Service** (deployed via `azd`).

> The actual feature spec (`Portfolio_Management_Feature_Spec.docx`) was provided as an attachment but could not be parsed directly. The feature breakdown below covers a standard portfolio-management feature set; update each section once the spec is dropped into `docs/`.

## Milestones

| # | Milestone | Outcome |
|---|---|---|
| M0 | Repo + scaffolding | Solution builds, web dev server runs, `azd up` provisions empty infra. ✅ done |
| M1 | Identity + Portfolio CRUD | Sign-in via Entra ID; user can create / rename / delete portfolios. |
| M2 | Transactions + Holdings | User records buy/sell; holdings auto-derived; running avg cost. |
| M3 | Watchlists | User curates symbols, see latest cached quote per item. |
| M4 | Quotes & Performance | Background quote refresher; portfolio P&L; basic charts. |
| M5 | Hardening | Migrations, audit logging, App Insights dashboards, error pages. |
| M6 | Optional | Import from CSV (broker statements); dividends; tax-lot accounting. |

## Feature breakdown (per-feature tasks)

### F1 — Authentication (M1)
- [ ] Register two Entra apps: `stocky-api` (exposes scope `api://stocky/access`) and `stocky-spa` (SPA platform with redirect `https://<web-host>/`).
- [ ] Grant the SPA delegated permission on the API scope.
- [ ] In `appsettings`: set `AzureAd:TenantId`, `AzureAd:ClientId` (API), `AzureAd:Audience = api://<api-client-id>`.
- [ ] In `src/Web`: add `@azure/msal-browser` + `@azure/msal-react`; configure `MsalProvider`.
- [ ] Add `LoginButton` / `useAccount` hook; attach Bearer to axios via interceptor.

### F2 — Portfolio CRUD (M1)
- [x] `Portfolio` entity + `StockyDbContext`.
- [x] `PortfoliosController` — GET list/by-id, POST, PUT, DELETE, GET performance.
- [ ] React routes: `/portfolios`, `/portfolios/:id`.
- [ ] List + create dialog (Mantine/MUI/AntD — pick one).
- [ ] EF migration `Init` + `await db.Database.MigrateAsync()` already wired in `Program.cs`.

### F3 — Transactions + Holdings (M2)
- [x] `Transaction`, `Holding` entities; transaction-driven holding recomputation in `TransactionsController.Create`.
- [ ] UI: transactions table, "Add transaction" dialog (Buy/Sell/Dividend/Deposit/Withdrawal/Split).
- [ ] Validation rules:
    - Sell qty ≤ current holding qty.
    - Negative numbers blocked; price ≥ 0.
- [ ] Server-side guard: re-check ownership + concurrency token on `Holding`.

### F4 — Watchlists (M3)
- [x] `Watchlist`, `WatchlistItem` entities + `WatchlistsController`.
- [ ] UI: per-user watchlists with add/remove symbol; show latest cached quote.

### F5 — Quotes + Market Data (M4)
- [x] `PriceQuote` entity + `QuotesController` (read-only over cached quotes).
- [ ] **QuoteRefresher** `BackgroundService` in API:
    - Pull distinct symbols from `Holdings ∪ WatchlistItems` every N minutes.
    - Call external market-data API (Alpha Vantage / Finnhub) using key from Key Vault.
    - Insert new `PriceQuote` rows; trim history > 30 days.
- [ ] Add `IDistributedCache` (Redis or in-memory) in front of provider calls for rate-limit protection.

### F6 — Portfolio Performance (M4)
- [x] `GET /api/portfolios/{id}/performance` computes market value, cost basis, unrealized P&L.
- [ ] Add timeseries endpoint `GET /api/portfolios/{id}/history?range=30d` once `PriceQuote` history exists.
- [ ] Recharts/ECharts chart in SPA.

### F7 — Observability (M5)
- [x] Application Insights provisioned and connection string injected via App Settings.
- [ ] Add OpenTelemetry + the Azure Monitor exporter (`Azure.Monitor.OpenTelemetry.AspNetCore`).
- [ ] Custom dimensions: `OwnerId` (hashed), `PortfolioId`, `Symbol`.
- [ ] App Insights workbook: API latency, 4xx/5xx, SQL DTU.

### F8 — CI/CD (M5)
- [ ] GitHub Actions workflow:
    - `dotnet test` + `npm run build`.
    - `azd provision` + `azd deploy` on push to `main` using federated credentials.
- [ ] Pull-request preview: `azd up` to ephemeral env when label `preview` is applied.

### F9 — Migrations & Seed (M5)
- [ ] Add an `IDesignTimeDbContextFactory<StockyDbContext>` so `dotnet ef migrations add` works against SQL.
- [ ] Initial migration: `dotnet ef migrations add Init` (already covered at startup via `MigrateAsync`).
- [ ] Optional: seed a "Sample Portfolio" for new users on first sign-in.

### F10 — Optional / Stretch (M6)
- [ ] CSV import (Fidelity/Schwab/Robinhood) → background queue (Azure Container Apps Job or Functions).
- [ ] Dividend reinvestment tracking.
- [ ] FIFO/LIFO tax-lot mode.
- [ ] Multi-currency FX conversion using a daily-snapshot table.

## Azure resources provisioned by `infra/main.bicep`

| Resource | Purpose | SKU (default) |
| --- | --- | --- |
| App Service Plan | Shared compute for Web + API | Linux B1 |
| App Service `stocky-{env}-api` | Hosts the .NET 10 API | DOTNETCORE 10.0 |
| App Service `stocky-{env}-web` | Hosts the React SPA via `pm2 serve --spa` | NODE 22 LTS |
| Azure SQL Server + DB `stocky` | Relational store | GP_S_Gen5_1 (Serverless) |
| User-Assigned Managed Identity | API → SQL/Key Vault auth | — |
| Key Vault | Stores market-data API keys etc. | Standard, RBAC mode |
| Application Insights + Log Analytics | Telemetry/logs | PerGB2018 |

## Post-provision manual steps

1. **Add the API MI as a SQL user**:
   ```sql
   CREATE USER [stocky-<env>-api-id-<token>] FROM EXTERNAL PROVIDER;
   ALTER ROLE db_datareader ADD MEMBER [stocky-<env>-api-id-<token>];
   ALTER ROLE db_datawriter ADD MEMBER [stocky-<env>-api-id-<token>];
   ALTER ROLE db_ddladmin   ADD MEMBER [stocky-<env>-api-id-<token>];
   ```
   Connect via SSMS / `sqlcmd` using your Entra account (set as SQL Entra admin during provisioning).
2. **Store market-data API key** in Key Vault, then add an App Setting on the API:
   `MarketData__ApiKey = @Microsoft.KeyVault(VaultName=...;SecretName=MarketDataApiKey)`
3. **Update Entra app registrations** with the deployed Web hostname (redirect URI + CORS).
4. **First azd deploy** publishes both services; verify `/health` returns `200`.
