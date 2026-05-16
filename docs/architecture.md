# Stocky — Architecture

> Stack: .NET 10 Web API · React 18 + Vite (TS) · Microsoft Entra ID · Azure SQL · Azure App Service · Bicep / azd.

## C4 — System Context

```mermaid
flowchart LR
    user([Investor])
    entra[Microsoft Entra ID]
    market[Market Data Provider<br/>e.g., Alpha Vantage / Finnhub]
    subgraph Stocky
        web[Web SPA<br/>React + Vite]
        api[Web API<br/>ASP.NET Core]
        db[(Azure SQL<br/>Stocky DB)]
    end
    user -- HTTPS --> web
    web -- OIDC / OAuth2 PKCE --> entra
    web -- Bearer JWT --> api
    api -- AAD token validation --> entra
    api -- TDS + Managed Identity --> db
    api -- HTTPS --> market
```

## C4 — Container View on Azure

```mermaid
flowchart TB
    subgraph RG[Resource Group: rg-stocky-&lt;env&gt;]
        plan[App Service Plan<br/>Linux B1]
        web[App Service: Web<br/>NODE 22 LTS · SPA]
        api[App Service: API<br/>DOTNETCORE 10.0]
        sql[Azure SQL Server<br/>+ Database 'stocky'<br/>Serverless GP_S_Gen5_1]
        kv[Key Vault<br/>RBAC mode]
        appi[Application Insights]
        law[Log Analytics Workspace]
        mi[User-Assigned<br/>Managed Identity]
    end
    user([Investor]) -- HTTPS --> web
    web -- /api/* (CORS) --> api
    api -- "AAD MI auth" --> sql
    api -- secrets --> kv
    api -- telemetry --> appi
    appi --> law
    api -. assigned .- mi
    mi -- "Key Vault Secrets User" --> kv
    mi -- "db_datareader/writer (Entra)" --> sql
```

## Domain Model

```mermaid
classDiagram
    class Portfolio {
        Guid Id
        string OwnerId
        string Name
        string BaseCurrency
        DateTimeOffset CreatedAt
    }
    class Holding {
        Guid Id
        Guid PortfolioId
        string Symbol
        decimal Quantity
        decimal AverageCost
    }
    class Transaction {
        Guid Id
        Guid PortfolioId
        string Symbol
        TransactionType Type
        decimal Quantity
        decimal Price
        decimal Fee
        DateTimeOffset ExecutedAt
    }
    class Instrument {
        string Symbol
        string Name
        string Exchange
        string Currency
        string AssetClass
    }
    class Watchlist {
        Guid Id
        string OwnerId
        string Name
    }
    class WatchlistItem {
        Guid Id
        Guid WatchlistId
        string Symbol
    }
    class PriceQuote {
        long Id
        string Symbol
        decimal Price
        decimal Change
        decimal ChangePercent
        DateTimeOffset AsOf
    }
    Portfolio "1" --> "*" Holding
    Portfolio "1" --> "*" Transaction
    Holding "*" --> "1" Instrument
    Watchlist "1" --> "*" WatchlistItem
    WatchlistItem "*" --> "1" Instrument
    PriceQuote "*" --> "1" Instrument
```

## Authentication Sequence

```mermaid
sequenceDiagram
    participant U as User (browser)
    participant SPA as Web SPA
    participant AAD as Entra ID
    participant API as Stocky API
    U->>SPA: Open app
    SPA->>AAD: /authorize (PKCE, scope=api://stocky/.default)
    AAD-->>U: Sign-in + consent
    AAD-->>SPA: Authorization code
    SPA->>AAD: /token (code + verifier)
    AAD-->>SPA: id_token + access_token
    SPA->>API: GET /api/portfolios + Bearer
    API->>AAD: JWKS / issuer validation (cached)
    API->>API: AuthZ + EF Core query (oid = ownerId)
    API-->>SPA: 200 [...]
```

## Request Flow — Record a Buy Transaction

```mermaid
sequenceDiagram
    participant SPA
    participant API
    participant DB as Azure SQL
    SPA->>API: POST /api/portfolios/{id}/transactions {type:Buy, symbol, qty, price}
    API->>DB: SELECT Portfolio + Holdings WHERE OwnerId=oid
    API->>API: Upsert Instrument (if new symbol)
    API->>API: Update Holding (recompute avg cost)
    API->>DB: INSERT Transaction; UPDATE Holding
    API-->>SPA: 201 TransactionDto
```

## Non-functional decisions

- **Multi-tenancy / data ownership**: every row owned by `OwnerId` = Entra `oid` claim. All queries filter by `OwnerId` from `User.GetOwnerId()` (`Data/UserContextExtensions.cs`).
- **Auth**: SPA uses MSAL.js (PKCE). API uses `Microsoft.Identity.Web` (`AddMicrosoftIdentityWebApi`) — token validation against the configured tenant.
- **Database access**: API → SQL via **User-Assigned Managed Identity** (Active Directory Managed Identity in the connection string). No SQL passwords at runtime.
- **Secrets**: held in Key Vault; API references them via Key Vault references in App Settings or `DefaultAzureCredential`.
- **Observability**: Application Insights auto-instrumentation; logs/metrics forwarded to a shared Log Analytics workspace.
- **Cost**: SQL Serverless (auto-pause 60 min) and a single B1 plan keeps idle cost minimal; scale up tiers via `infra/main.bicep` parameters when ready.
- **CORS**: API only accepts the deployed Web hostname (set via `AllowedOrigins__0` in `main.bicep`) plus `http://localhost:5173` for local dev.

## Environment overview

| Component | Local | Azure |
| --- | --- | --- |
| Web | `vite dev` @ 5173 | App Service Linux (NODE 22), `pm2 serve --spa` |
| API | `dotnet run` @ 5xxx | App Service Linux (DOTNETCORE 10.0), `/health` probe |
| DB  | EF InMemory fallback | Azure SQL Serverless |
| Auth | Entra ID (dev tenant) | Entra ID (prod tenant) |
| Secrets | user-secrets | Key Vault + MI |
