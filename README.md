# FreeCICD

Azure DevOps pipeline monitoring dashboard built on the FreeCRM framework with ASP.NET Core and Blazor WebAssembly (.NET 10). A background service polls Azure DevOps every 5 seconds, diffs pipeline statuses, and pushes live updates to all connected browsers via SignalR — giving operations teams a real-time view of build and release health without refreshing the Azure DevOps portal.

## What it does

- **Live pipeline dashboard** — `PipelineMonitorService` polls the Azure DevOps REST API on a 5-second interval, computes status diffs, and broadcasts changes to the `PipelineMonitor` SignalR group
- **Real-time browser updates** — SignalR hub (`/freecicdHub`) pushes pipeline state to every connected client immediately on change
- **Full FreeCRM scaffold** — multi-tenant auth (local, OpenID Connect, Google, Facebook, Microsoft, Apple), user/department/settings management, Roslyn plugin system, file storage
- **Azure DevOps API integration** — dedicated `FreeCICD.App.API.cs` controller endpoints for querying pipelines, builds, and releases

## Projects

| Project | Role |
|---------|------|
| `FreeCICD` | ASP.NET Core host; `PipelineMonitorService`, SignalR hub, REST API, auth, plugin loader |
| `FreeCICD.Client` | Blazor WASM client; pipeline dashboard, standard admin pages |
| `FreeCICD.DataAccess` | Business logic; Azure DevOps API calls, EF Core, auth helpers |
| `FreeCICD.DataObjects` | Shared DTOs; pipeline models, endpoint constants |
| `FreeCICD.EFModels` | EF Core DbContext; core framework tables |
| `FreeCICD.Plugins` | Roslyn dynamic C# plugin runtime |

## Quick start

```bash
cd FreeCICD/FreeCICD
dotnet run
```

Configure your Azure DevOps connection in `appsettings.json` or via .NET user secrets:

```bash
dotnet user-secrets set "AzureDevOps:Organization" "<org>"
dotnet user-secrets set "AzureDevOps:PersonalAccessToken" "<pat>"
```

Navigate to `https://localhost:5001`.

## Build details

| Property | Value |
|---|---|
| Target framework | net10.0 |
| Database backends | SQL Server, MySQL, PostgreSQL, SQLite, InMemory |
| Auth providers | Cookie, OpenID Connect, Microsoft, Google, Facebook, Apple |
| Real-time | SignalR (local) or Azure SignalR Service |

## License

Released under the [MIT License](https://opensource.org/licenses/MIT).

## About

Designed, written, and implemented by **Washington State University - Enrollment Information Technology (WSU-EIT)**.

- Website: https://em.wsu.edu/eit/
- GitHub: https://github.com/WSU-EIT