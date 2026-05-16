# Stocky

Portfolio management web app for tracking stock investments. Built as a .NET 10 Web API + React (Vite + TypeScript) frontend, secured by Microsoft Entra ID, persisted in Azure SQL, and deployed to Azure App Service via the Azure Developer CLI (`azd`).

## Repository layout

```
.
├── azure.yaml                # azd service map (api + web)
├── infra/
│   ├── main.bicep            # App Service plan, API + Web sites, Azure SQL,
│   │                         # Key Vault, App Insights, Log Analytics, MI + RBAC
│   └── main.parameters.json
├── src/
│   ├── Api/                  # ASP.NET Core Web API (controllers + EF Core)
│   │   ├── Controllers/      # Portfolios, Holdings, Transactions, Watchlists, Quotes, Health
│   │   ├── Domain/           # Aggregate entities
│   │   ├── Data/             # StockyDbContext, user-context helpers
│   │   └── Dtos/             # Request/response contracts
│   └── Web/                  # Vite React + TypeScript SPA
├── docs/
│   ├── architecture.md       # C4 + sequence diagrams (Mermaid)
│   └── implementation-plan.md
└── Stocky.sln
```

## Local development

### Prerequisites
- .NET SDK 10
- Node 22+ / npm 11+
- Azure CLI + `azd`

### Run the API
```powershell
cd src/Api
dotnet run
```
The API listens on `http://localhost:5xxx` (see `Properties/launchSettings.json`). If `ConnectionStrings:Sql` is empty the API uses an EF in-memory store so you can boot without SQL.

### Run the Web app
```powershell
cd src/Web
npm install
npm run dev
```
The dev server is `http://localhost:5173` (already allowed by the API CORS policy in `appsettings.Development.json`).

## Deploy to Azure

```powershell
azd auth login
azd up
```
The first `azd up` prompts for environment name, subscription, and region, then provisions the resource group with the topology described in `docs/architecture.md`.

Required environment values (set via `azd env set` before `azd up`):
| Variable | Description |
| --- | --- |
| `ENTRA_TENANT_ID` | Tenant Id used to validate API access tokens |
| `ENTRA_API_CLIENT_ID` | App registration Client Id for the API |
| `SQL_ADMIN_LOGIN` | SQL admin login (provision-time only) |
| `SQL_ADMIN_PASSWORD` | SQL admin password (provision-time only) |
| `SQL_ENTRA_ADMIN_OBJECT_ID` | Object Id of the Entra group/user that owns SQL (optional but recommended) |
| `SQL_ENTRA_ADMIN_LOGIN` | Display name of that principal |

At runtime the API connects to SQL using its User-Assigned Managed Identity, so the SQL password is never used after provisioning.

See [docs/implementation-plan.md](docs/implementation-plan.md) for the end-to-end roadmap.
