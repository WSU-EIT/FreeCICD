# FreeCICD.DataAccess

## Overview

The `FreeCICD.DataAccess` project is the **business logic layer** containing all data operations, authentication, encryption, integrations, and cross-cutting concerns. It implements the `IDataAccess` interface and uses Entity Framework Core for persistence.

---

## Project Structure

```
FreeCICD.DataAccess/
+-- DataAccess.cs                  # Core initialization and configuration
+-- DataAccess.ActiveDirectory.cs  # LDAP/AD integration
+-- DataAccess.Ajax.cs             # AJAX endpoint helpers
+-- DataAccess.App.cs              # App-specific extension point
+-- DataAccess.ApplicationSettings.cs # Application settings management
+-- DataAccess.Authenticate.cs     # Authentication logic
+-- DataAccess.CSharpCode.cs       # Dynamic C# compilation
+-- DataAccess.Departments.cs      # Department CRUD
+-- DataAccess.Disposable.cs       # IDisposable implementation
+-- DataAccess.Encryption.cs       # Encryption utilities
+-- DataAccess.FileStorage.cs      # File management
+-- DataAccess.JWT.cs              # JWT token generation/validation
+-- DataAccess.Language.cs         # Internationalization
+-- DataAccess.Migrations.cs       # Database migration runner
+-- DataAccess.Plugins.cs          # Plugin system integration
+-- DataAccess.SeedTestData.cs     # Initial data seeding
+-- DataAccess.Settings.cs         # Settings key-value store
+-- DataAccess.SignalR.cs          # Real-time communication
+-- DataAccess.Tags.cs             # Tag system operations
+-- DataAccess.Tenants.cs          # Multi-tenant operations
+-- DataAccess.UDFLabels.cs        # User-defined fields
+-- DataAccess.UserGroups.cs       # User group management
+-- DataAccess.Users.cs            # User CRUD operations
+-- DataAccess.Utilities.cs        # Utility methods
+-- DataMigrations.MySQL.cs        # MySQL-specific migrations
+-- DataMigrations.PostgreSQL.cs   # PostgreSQL-specific migrations
+-- DataMigrations.SQLite.cs       # SQLite-specific migrations
+-- DataMigrations.SQLServer.cs    # SQL Server-specific migrations
+-- GlobalUsings.cs                # Global using statements
+-- GraphAPI.cs                    # Microsoft Graph integration
+-- GraphAPI.App.cs                # App-specific Graph extensions
+-- RandomPasswordGenerator.cs     # Password generation
+-- RandomPasswordGenerator.App.cs # App-specific password rules
+-- Utilities.cs                   # Static utility methods
+-- Utilities.App.cs               # App-specific utilities
+-- FreeCICD.DataAccess.csproj     # Project file
```

---

## Architecture Diagram

```
+-----------------------------------------------------------------------------+
|                         DATA ACCESS LAYER                                   |
+-----------------------------------------------------------------------------+
|                                                                             |
|  +-----------------------------------------------------------------------+  |
|  |                        IDataAccess Interface                          |  |
|  |  +-----------+  +-----------+  +-----------+  +-----------+          |  |
|  |  | Authenticate|  |    Users    |  |   Tenants   |  |  Departments|   |  |
|  |  | GetUser     |  | SaveUser    |  | GetTenant   |  | SaveDept    |   |  |
|  |  | GetToken    |  | DeleteUser  |  | SaveTenant  |  | GetDepts    |   |  |
|  |  +-----------+  +-----------+  +-----------+  +-----------+          |  |
|  |                                                                       |  |
|  |  +-----------+  +-----------+  +-----------+  +-----------+          |  |
|  |  | FileStorage |  |   Settings  |  |   SignalR   |  |   Plugins   |   |  |
|  |  | SaveFile    |  | GetSetting  |  |SignalRUpdate|  | GetPlugins  |   |  |
|  |  | GetFile     |  | SaveSetting |  |             |  | RunPlugin   |   |  |
|  |  +-----------+  +-----------+  +-----------+  +-----------+          |  |
|  +-----------------------------------------------------------------------+  |
|                                    |                                        |
|                                    |                                        |
|  +-----------------------------------------------------------------------+  |
|  |                      DataAccess Implementation                        |  |
|  |                                                                       |  |
|  |  +---------------------------------------------------------------+   |  |
|  |  |                    Cross-Cutting Concerns                     |   |  |
|  |  |  +-----------+  +-----------+  +-----------+  +-----------+   |   |  |
|  |  |  |Encryption |  |    JWT    |  |  Hashing  |  |  Caching  |   |   |  |
|  |  |  +-----------+  +-----------+  +-----------+  +-----------+   |   |  |
|  |  +---------------------------------------------------------------+   |  |
|  |                                                                       |  |
|  |  +---------------------------------------------------------------+   |  |
|  |  |                      External Integrations                    |   |  |
|  |  |  +-----------+  +-----------+  +-----------+  +-----------+   |   |  |
|  |  |  |   LDAP    |  |Graph API  |  |  SignalR  |  |  Plugins  |   |   |  |
|  |  |  +-----------+  +-----------+  +-----------+  +-----------+   |   |  |
|  |  +---------------------------------------------------------------+   |  |
|  +-----------------------------------------------------------------------+  |
|                                    |                                        |
|                                    |                                        |
|  +-----------------------------------------------------------------------+  |
|  |                        EFDataModel (DbContext)                        |  |
|  |              FreeCICD.EFModels - Entity Framework Core                |  |
|  +-----------------------------------------------------------------------+  |
|                                                                             |
+-----------------------------------------------------------------------------+
```

---

## Authentication Flow

```
+-----------------------------------------------------------------------------+
|                         AUTHENTICATION FLOW                                 |
+-----------------------------------------------------------------------------+

    Client                    DataAccess                    Database
      |                           |                            |
      |  Authenticate(creds)      |                            |
      | ------------------------> |                            |
      |                           |                            |
      |                           |  Find User by Email/Username
      |                           | -------------------------> |
      |                           | <------------------------- |
      |                           |                            |
      |                           |  Check Lockout Status      |
      |                           |  (FailedLoginAttempts,     |
      |                           |   LastLockoutDate)         |
      |                           |                            |
      |                     +-------------+                    |
      |                     |  Locked?  |                      |
      |                     +-------------+                    |
      |                           |                            |
      |          +------------------------------------+        |
      |         YES                               NO           |
      |          |                                 |           |
      |          |                                 |           |
      |   Return Error              Validate Password          |
      |   "Account Locked"          (HashPasswordValidate)     |
      |                                            |           |
      |                     +-------------------------------------->
      |                     |                              |   |
      |                   VALID                        INVALID |
      |                     |                              |   |
      |                     |                              |   |
      |              Clear Lockout               Increment Failed
      |              Generate JWT                Update Lockout |
      |              Update LastLogin                      |   |
      |                     |                              |   |
      | <------------------ |                              |   |
      |   User + AuthToken                                 |   |
      |                                                    |   |
      | <------------------------------------------------- |   |
      |   Error: Invalid credentials                           |
      |                                                        |
```

---

## Security Features

### Password Hashing

```csharp
// Password hashing uses BCrypt
string hashedPassword = HashPassword(plainTextPassword);
bool isValid = HashPasswordValidate(plainTextPassword, hashedPassword);
```

### Account Lockout

```
+-----------------------------------------------------------------------+
|                    ACCOUNT LOCKOUT POLICY                             |
+-----------------------------------------------------------------------+
|  Max Failed Attempts:  5                                             |
|  Lockout Duration:     10 minutes                                    |
|                                                                       |
|  After 5 failed attempts:                                            |
|  -> LastLockoutDate = DateTime.UtcNow                                |
|  -> Account locked for 10 minutes                                    |
|                                                                       |
|  On successful login:                                                |
|  -> FailedLoginAttempts = null                                       |
|  -> LastLockoutDate = null                                           |
+-----------------------------------------------------------------------+
```

### JWT Token Generation

```csharp
// Token includes:
// - TenantId
// - UserId
// - Fingerprint (client identifier)
// - Sudo flag (admin impersonation)
string token = GetUserToken(tenantId, userId, fingerprint, isSudo);
```

### Sudo Login (Admin Impersonation)

```
Username format: "sudo username@domain.com"
Password: Admin user's password

Allows tenant admins to log in as any user in their tenant.
```

---

## Key Interface Methods

### IDataAccess Interface

```csharp
public partial interface IDataAccess
{
    // Authentication
    Task<DataObjects.User> Authenticate(DataObjects.Authenticate authentication, string fingerprint);
    string GetUserToken(Guid tenantId, Guid userId, string fingerprint, bool sudo);
    
    // Users
    Task<DataObjects.User> GetUser(Guid userId);
    Task<DataObjects.User> SaveUser(DataObjects.User user, DataObjects.User currentUser);
    Task<DataObjects.BooleanResponse> DeleteUser(Guid userId, DataObjects.User currentUser);
    
    // Tenants
    Task<DataObjects.Tenant> GetTenant(Guid tenantId);
    Task<DataObjects.Tenant> SaveTenant(DataObjects.Tenant tenant, DataObjects.User currentUser);
    
    // Files
    Task<DataObjects.FileStorage> GetFile(Guid fileId);
    Task<DataObjects.FileStorage> SaveFile(DataObjects.FileStorage file, DataObjects.User currentUser);
    
    // Settings
    Task<DataObjects.Setting> GetSetting(string settingName, Guid? tenantId, Guid? userId);
    Task<DataObjects.Setting> SaveSetting(DataObjects.Setting setting, DataObjects.User currentUser);
    
    // SignalR
    Task SignalRUpdate(DataObjects.SignalRUpdate update);
    
    // Plugins
    Task<List<Plugins.Plugin>> GetPlugins(Guid tenantId);
}
```

---

## Database Provider Support

```
+-----------------------------------------------------------------------------+
|                         DATABASE INITIALIZATION                             |
+-----------------------------------------------------------------------------+

    Constructor(ConnectionString, DatabaseType)
           |
           |
    +---------------+
    |  DatabaseType?   |
    +---------------+
             |
    +-------------------------------+
    |        |        |        |        |
    |        |        |        |        |
 InMemory SQLServer PostgreSQL MySQL  SQLite
    |        |        |        |        |
    |        |        |        |        |
    +-------------------------------+
                     |
                     |
           +-----------------+
           |  CanConnect()?  |
           +-----------------+
                    |
           +--------------+
          YES              NO
           |                |
           |                |
    Apply Migrations   EnsureCreated()
           |                |
           +--------------+
                   |
                   |
            SeedTestData()
```

---

## External Integrations

### Microsoft Graph API

```csharp
// Email sending via Microsoft Graph
GraphAPI.SendEmail(mailServerConfig, emailMessage);
```

### LDAP/Active Directory

```csharp
// User lookup from Active Directory
var adUser = LookupUserInActiveDirectory(username, tenantSettings);
```

### SignalR Real-Time Updates

```csharp
// Broadcast updates to connected clients
await SignalRUpdate(new DataObjects.SignalRUpdate {
    TenantId = tenantId,
    UpdateType = DataObjects.SignalRUpdateType.User,
    ItemId = userId,
    Message = "User profile updated"
});
```

---

## Extension Points

### App-Specific Extensions (DataAccess.App.cs)

```csharp
public partial class DataAccess
{
    // Override these methods for app-specific logic
    private void DataAccessAppInit() { }
    
    private async Task<DataObjects.BooleanResponse> DeleteRecordsApp(object Rec, DataObjects.User? CurrentUser) { }
    
    private void GetDataApp(object Rec, object DataObject, DataObjects.User? CurrentUser) { }
    
    private void SaveDataApp(object Rec, object DataObject, DataObjects.User? CurrentUser) { }
}
```

### FreeCICD Extensions (DataAccess.App.FreeCICD.cs)

```csharp
// Azure DevOps integration methods
Task<List<DataObjects.DevopsProjectInfo>> GetDevOpsProjectsAsync(string pat, string orgName);
Task<DataObjects.BuildDefinition> CreateOrUpdateDevopsPipeline(...);
```

---

## Dependencies

```xml
<ItemGroup>
  <!-- Azure -->
  <PackageReference Include="Azure.Identity" Version="1.17.1" />
  
  <!-- Data Processing -->
  <PackageReference Include="Brad.Wickett_Sql2LINQ" Version="3.0.1" />
  <PackageReference Include="CsvHelper" Version="33.1.0" />
  
  <!-- Security -->
  <PackageReference Include="JWTHelpers" Version="1.0.1" />
  
  <!-- Microsoft Graph -->
  <PackageReference Include="Microsoft.Graph" Version="5.97.0" />
  
  <!-- Blazor -->
  <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.Server" Version="10.0.0" />
  
  <!-- LDAP -->
  <PackageReference Include="Novell.Directory.Ldap.NETStandard" Version="4.0.0" />
  
  <!-- PDF Generation -->
  <PackageReference Include="QuestPDF" Version="2025.7.4" />
</ItemGroup>
```

---

## Initialization Sequence

```
1. DataAccess Constructor
   |
   +-> Configure DbContext with provider
   |
   +-> Check database connectivity
   |   |
   |   +-> If connected: Apply migrations
   |   |
   |   +-> If not connected: EnsureCreated()
   |
   +-> SeedTestData() - Create default tenant/admin
   |
   +-> Load plugins from cache
   |
   +-> Set GlobalSettings.StartupRun = true
```

---

## Best Practices

1. **Use async/await**: All data operations should be async
2. **Filter by TenantId**: Every query must be tenant-scoped
3. **Check soft deletes**: Filter `Deleted == false` by default
4. **Use CurrentUser**: Pass current user for audit trails
5. **Handle exceptions**: Catch and log all database errors
6. **Use SignalR updates**: Broadcast changes for real-time UI
7. **Extend via App files**: Don't modify core DataAccess files

---

## Security Checklist

- [ ] All passwords are hashed (never stored plaintext)
- [ ] Account lockout is enforced after failed attempts
- [ ] JWT tokens include fingerprint for binding
- [ ] Sensitive settings are encrypted
- [ ] Tenant isolation is enforced on all queries
- [ ] Admin operations require Admin flag
- [ ] Sudo login is audited
