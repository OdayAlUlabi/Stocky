# Stocky — Implementation Plan

Turns the two product docs into shippable milestones.

- **Feature spec**: `docs/Portfolio_Management_Feature_Spec.docx` — 79 features across 7 modules (17 MVP must-haves).
- **UI spec**: `docs/Portfolio_UI_Screen_Spec.docx` — 16 screens (SCR-000 … SCR-040) with wireframes & state diagrams.
- **Stack**: .NET 10 Web API · React 19 (Vite + TS) · EF Core 10 · Azure SQL · Microsoft Entra ID · Azure App Service (Linux B1) · `azd`.

## MVP scope (this implementation pass)

| Screen | Spec | Scope this pass |
|---|---|---|
| SCR-000 | Global app shell | Sidebar + topbar + protected layout. Market ticker static placeholder. |
| SCR-001 | Login / Register / 2FA | Entra ID redirect (Entra handles 2FA via policy). |
| SCR-002 | Dashboard | KPIs + value chart + sector pie + top movers from real holdings. News/events deferred. |
| SCR-003 | Positions list | Full table with cost basis & weight; CSV export. |
| SCR-004 | Add/Edit Trade drawer | BUY / SELL / DIVIDEND with validation. CSV import deferred. |
| SCR-006 | Watchlist | Multiple lists; add/remove symbols; latest cached quote. |

Deferred: SCR-005, SCR-007, SCR-008, SCR-009, SCR-010, SCR-020, SCR-021, SCR-030, SCR-031, SCR-040.

## Milestones

| # | Milestone | Outcome |
|---|---|---|
| M0 | Repo + scaffolding | `azd up` provisions empty infra. ✅ done |
| M1 | Identity + Portfolio CRUD | Entra sign-in; create/rename/delete portfolios. |
| M2 | Transactions + Holdings | Buy/Sell/Dividend with validation; auto-derived holdings. |
| M3 | Watchlists | Multiple lists; add/remove. |
| M4 | Web UI shell + 5 MVP screens | Login → Dashboard → Portfolios → Positions → Add Trade → Watchlist. |
| M5 | Market data + observability | Quote refresher, OpenTelemetry, dashboards. |
| M6 | High-priority screens | Position detail, reports, alerts. |
| M7 | Stretch | Screener, charts, analytics, rebalancing, CSV import, mobile. |

---

## Backend tasks

### API gaps closed this pass
- [x] Portfolios CRUD + per-portfolio performance.
- [x] Holdings list.
- [x] Watchlists CRUD.
- [x] Quotes read-only.
- [x] **Transactions**: add `PUT` and `DELETE` so SCR-004 edit/delete flows work.
- [x] **Transactions**: validate `Quantity > 0`, `Price >= 0`, `Fee >= 0`, no future `ExecutedAt`, `SELL` qty ≤ current holding qty.
- [x] **Dashboard aggregate**: `GET /api/dashboard?portfolioId=` returning KPIs, allocations, top movers, value history.
- [x] **Securities search**: `GET /api/securities/search?q=` backed by `Instruments` table + small static seed; swap for Finnhub later.

### Backend tasks (later milestones)
- [x] `QuoteRefresher` `BackgroundService` — Alpaca v2 snapshots (Finnhub/FMP swap remains a config-only change), secret from Key Vault.
- [x] `IDistributedCache` in front of provider calls (Redis when `Cache:RedisConnectionString`, in-memory distributed cache otherwise; news + bars now share L2 across instances).
- [x] `Alert` entity + alert engine (SCR-020).
- [x] Tax lots (SCR-005 + SCR-021 cap-gains).
- [x] Daily snapshot job → `portfolio_snapshots` (replaces synthesized SCR-002 history).
- [x] OpenTelemetry + Azure Monitor exporter, custom dimensions `stocky.owner_hash`, `stocky.portfolio_id`, `stocky.symbol` (set by `TelemetryEnricherMiddleware`).
- [x] `IDesignTimeDbContextFactory<StockyDbContext>`.

---

## Web tasks

### Dependencies added this pass
- `react-router-dom` — routing.
- `@azure/msal-browser` + `@azure/msal-react` — Entra ID.
- `@mantine/core` + `@mantine/hooks` + `@mantine/notifications` + `@mantine/dates` + `@tabler/icons-react` — UI kit.
- `@tanstack/react-query` — data fetching.
- `recharts` — charts.
- `dayjs` — dates.

### Architecture
- `src/auth/` — MSAL config + `useApiToken()` + `RequireAuth` guard.
- `src/api/` — typed fetch client with bearer interceptor; one file per resource.
- `src/routes/` — one folder per SCR (`shell/`, `login/`, `dashboard/`, `portfolios/`, `watchlist/`).
- `src/components/` — shared design-system pieces (`MetricCard`, `EmptyState`, `TickerSearch`).
- Routes:
  - `/login` → SCR-001
  - `/` (protected, wrapped by AppShell):
    - `/` → SCR-002 Dashboard
    - `/portfolios` → list
    - `/portfolios/:id` → SCR-003 positions (drawer = SCR-004)
    - `/watchlist` → SCR-006

---

## Azure resources (already provisioned)

| Resource | Purpose | SKU |
| --- | --- | --- |
| App Service Plan | Linux compute Web + API | B1 |
| App Service `stocky-{env}-api` | .NET 10 API | DOTNETCORE 10.0 |
| App Service `stocky-{env}-web` | React SPA via `pm2 serve --spa` | NODE 22 LTS |
| Azure SQL Server + DB | Relational store | GP_S_Gen5_1 (Serverless) |
| User-Assigned MI | API → SQL/Key Vault auth | — |
| Key Vault | Secrets | Standard, RBAC |
| App Insights + Log Analytics | Telemetry/logs | PerGB2018 |

## Post-provision manual steps

1. **Add the API MI as a SQL user**:
   ```sql
   CREATE USER [stocky-<env>-api-id-<token>] FROM EXTERNAL PROVIDER;
   ALTER ROLE db_datareader ADD MEMBER [stocky-<env>-api-id-<token>];
   ALTER ROLE db_datawriter ADD MEMBER [stocky-<env>-api-id-<token>];
   ALTER ROLE db_ddladmin   ADD MEMBER [stocky-<env>-api-id-<token>];
   ```
2. Register `stocky-api` + `stocky-spa` Entra apps; grant SPA delegated permission on API scope.
3. Set `AzureAd:*` on API and `VITE_ENTRA_*` on Web.
4. Store market-data API key in Key Vault → `MarketData__ApiKey = @Microsoft.KeyVault(...)`.
5. First `azd deploy` — verify `/health` returns 200.

## Post-MVP backlog (from full spec)

- **Portfolio**: broker sync (Plaid/Alpaca), CSV/OFX import, multi-currency FX, crypto exchange sync, options Greeks, dividend automation, corporate actions, tax-lot methods ✅ (FIFO / LIFO / HighestCost / LowestCost selectable per portfolio).
- **Analytics**: screener ✅ (sector/country/marketcap/divyield/beta filters via `/api/securities/screener`), TradingView Lightweight Charts, analyst ratings, earnings calendar, correlation matrix, risk metrics, backtesting.
- **Data**: real-time quotes, Level 2, news, SEC filings, insider trades, short interest, economic calendar, options flow.
- **Trading**: live order execution, advanced order types, paper trading, automated trading, rebalancing ✅ (`RebalanceService` + `/rebalance` + target editor UI), margin.
- **Reporting**: TWRR/MWRR ✅ (chain-linked daily TWRR + XIRR MWRR in `PerformanceController`), capital gains with wash-sale ✅ (`WashSaleService` + `/wash-sales` endpoint + UI), dividend report ✅, advisor sharing.
- **Alerts**: price / technical / earnings / news / drift / insider, multi-channel delivery, history.
- **Admin**: mobile, public REST API, WebSocket streaming, RBAC, dark mode, i18n.

Free data providers identified in the spec (integration order): **Finnhub** → **FMP** → **Polygon.io** → **Alpaca** → **CoinGecko** → **SEC EDGAR** → **ExchangeRate-API** → **FRED**.
