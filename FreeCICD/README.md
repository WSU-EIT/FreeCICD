# FreeCICD

The `FreeCICD` project is the ASP.NET Core server host for the FreeCICD application. It wires together the Blazor WebAssembly client, REST API controllers, a SignalR real-time hub, Azure DevOps pipeline management, authentication middleware, a plugin system, and a background pipeline monitor service.

## What the code does

`Program.cs` builds the ASP.NET Core host using `Microsoft.NET.Sdk.Web`. On startup it:

- Registers Blazor WebAssembly components with interactive render mode, serving the `FreeCICD.Client` assembly.
- Configures SignalR — falls back to local SignalR unless `AzureSignalRurl` is set; adds a `SignalRPolicy` CORS policy so clients on different origins (e.g., an Aspire AppHost) can connect.
- Loads compiled plugin assemblies from the `Plugins/` directory using `Plugins.Plugins`; server references and `using` statements are supplied from configuration.
- Resolves the database connection string and type (`InMemory`, SQLite, SQL Server, MySQL, PostgreSQL) and registers `IDataAccess` as transient DI.
- Registers a `PipelineMonitorService` hosted service that polls Azure DevOps every 5 seconds and broadcasts status diffs to SignalR clients subscribed to the `PipelineMonitor` group.
- Registers a `CustomAuthentication` service supporting local accounts, OpenID Connect (with optional HTTPS scheme forcing for WSU login endpoints), and social OAuth providers (Google, Facebook, Microsoft, Apple).
- Configures claim-based authorization policies: `AppAdmin`, `Admin`, `CanBeScheduled`, `ManageAppointments`, `ManageFiles`, and `PreventPasswordChange`.
- Maps the `freecicdHub` SignalR endpoint at `/freecicdHub` with stateful reconnects enabled.
- Configures the QuestPDF community license.

Controllers cover: AJAX, app-specific Azure DevOps endpoints (`FreeCICD.App.API.cs`), application settings, authentication, departments, encryption, file storage, languages, plugins, tags, tenants, UDF labels, user groups, users, utilities, authorization callbacks, and setup.

`Program.App.cs` contains application-specific startup customizations (authentication providers, configuration helper loading, authorization policies).

## Key public classes

| Class | Description |
|-------|-------------|
| `Program` | Application entry point; configures all services and middleware |
| `PipelineMonitorService` | `BackgroundService` that polls Azure DevOps and pushes pipeline diffs via SignalR |
| `freecicdHub` | SignalR hub mapped at `/freecicdHub`; tracks subscribers for the monitor group |
| `CustomAuthenticationHandler` | JWT/cookie authentication middleware |
| `CustomAuthIdentity` | Identity helper for building `ClaimsPrincipal` from tokens |
| `RouteHelper` | Route configuration utilities |
| `DataController` | Base REST API controller with DI wiring |
| `SetupController` | Handles initial application setup flow |

## Blazor server components

Razor components are in `Components/`. The application root is `App.razor`. Server-side components complement the WebAssembly client pages listed in the `FreeCICD.Client` README.

## @page routes (server-rendered)

The server hosts standard ASP.NET Core Razor Pages (`/Error`, authorization callback pages) plus the WebAssembly shell. All application routes are handled by the client project.

## Project references and NuGet packages

**Project references:**
- `FreeCICD.DataAccess`
- `FreeCICD.Plugins`
- `FreeCICD.Client`

**Notable NuGet packages:**

| Package | Version |
|---------|---------|
| `Microsoft.AspNetCore.Components.WebAssembly.Server` | 10.0.0 |
| `Microsoft.Azure.SignalR` | 1.32.0 |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | 10.0.0 |
| `Microsoft.AspNetCore.Authentication.Google` | 10.0.0 |
| `Microsoft.AspNetCore.Authentication.Facebook` | 10.0.0 |
| `Microsoft.AspNetCore.Authentication.MicrosoftAccount` | 10.0.0 |
| `AspNet.Security.OAuth.Apple` | 10.0.0 |

## Build details

| Field | Value |
|-------|-------|
| SDK | `Microsoft.NET.Sdk.Web` |
| Target framework | `net10.0` |
| Output type | Web application (executable) |
| Nullable | enabled |

## License

Released under the [MIT License](https://opensource.org/licenses/MIT).

## About

Designed, written, and implemented by **Washington State University - Enrollment Information Technology (WSU-EIT)**.

- Website: https://em.wsu.edu/eit/
- GitHub: https://github.com/WSU-EIT

Part of the [FreeCICD](../README.md) solution.
