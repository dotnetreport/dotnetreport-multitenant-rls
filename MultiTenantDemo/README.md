# Multi-tenant demo app

A runnable ASP.NET Core (.NET 8) app that shows Dotnet Report's multi-tenancy and row-level security with a realistic
user model: users in several tenants, a reporting hierarchy, roles with different data scopes, and impersonation.

Built on the `dotnetreport` NuGet package (6.3.4). The only change to the installed Dotnet Report code is
`GetSettings()` in [`Controllers/DotNetReportApiController.cs`](Controllers/DotNetReportApiController.cs).

## What's in the database

`Database/CreateMultiTenantDemoDb.sql` creates **MultiTenantDemo**:

| Object | Purpose |
|---|---|
| `dbo.Tenants` | 5 tenants: ACME, GLOBEX, INITECH, CONTOSO, FABRIKAM |
| `app.Roles` | 6 roles, each with a **DataScope**: `AllTenants`, `AssignedTenants`, `Team`, `Self` |
| `app.Users` | 26 users; `ManagerUserId` builds the hierarchy (it can cross tenants) |
| `app.UserRoles`, `app.UserTenants` | role membership and which tenants each user can access |
| `dbo.Customers`, `Products`, `Orders`, `OrderItems` | ~1,100 orders / 2,700 lines. Every table has `TenantId`; sales tables also have `SalesRepId` |
| `dbo.SalesReps`, `dbo.OrderDetails` (views) | report-friendly views that keep `TenantId` / `SalesRepId` |

Every user's password is `Demo123!`. The `app.*` tables are for the app, not for reporting.

## How the filters are built

At sign-in `AccessProfile` works out what the user may see and issues it as claims. The only change to the shipped
Dotnet Report code is `GetSettings()` in `DotNetReportApiController`, which reads those claims inline:

```csharp
var tenantIds = user.FindFirst("tenant_ids")?.Value;          // e.g. "1" or "1,2"
var salesRepIds = user.FindFirst("salesrep_ids")?.Value;      // e.g. "12,13,14"; absent for admins and support
if (string.IsNullOrEmpty(tenantIds)) tenantIds = "-1";         // fail closed: no tenant, no rows
if (string.IsNullOrEmpty(salesRepIds))
    settings.DataFilters = new { TenantId = tenantIds };
else
    settings.DataFilters = new { TenantId = tenantIds, SalesRepId = salesRepIds };
```

| User | Scope | DataFilters sent to Dotnet Report |
|---|---|---|
| sarah.chen@dotnetreport.demo, PlatformAdmin | every tenant | `TenantId IN (1,2,3,4,5)` |
| mike.rivera@dotnetreport.demo, SupportAnalyst | 3 assigned tenants | `TenantId IN (1,2,3)` |
| karen.walsh@contoso.demo, TenantAdmin | Contoso + Fabrikam | `TenantId IN (4,5)` |
| linda.park@acme.demo, RegionalDirector | her org tree across ACME + GLOBEX | `TenantId IN (1,2)` `SalesRepId IN (3,11,21,12,15,...)` |
| rachel.kim@acme.demo, SalesManager | herself + 2 reps | `TenantId IN (1)` `SalesRepId IN (12,13,14)` |
| jake.wilson@acme.demo, SalesRep | his own rows | `TenantId IN (1)` `SalesRepId IN (13)` |

All passwords are `Demo123!`. Every user and email is listed on the Users & Access page.

`ClientId` is the active tenant's code (e.g. `ACME`), so reports a tenant saves stay in that tenant. The platform
admin's "All my tenants" view uses `ClientId = ""`, so reports Sarah builds there are shared with every tenant, and each
viewer still only gets their own rows. Users in more than one tenant get a tenant switcher in the navbar.

## Reports, folders and dashboards (report-level access)

Row-level security decides *which rows*; Manage Access decides *which reports*. The demo account has:

| Folder | Who can see it | Reports |
|---|---|---|
| Sales Team | every role (view only) | Sales by Rep, Monthly Revenue (line), Revenue by Category (pie), Top Customers, Order Line Details |
| Leadership | TenantAdmin, RegionalDirector, SalesManager | Revenue by Tenant (bar), Team Performance by Manager, **North America QBR** (named users only: Linda, Tom, Grace) |
| Operations & Support | SupportAnalyst, TenantAdmin | Orders by Status (bar) |
| Acme Workspace | **ACME tenant only** (`ClientId = ACME`) | Acme Product Sales |

| Dashboard | Who can see it |
|---|---|
| Sales Overview | every role |
| Leadership Overview | TenantAdmin, RegionalDirector, SalesManager |

PlatformAdmin manages everything. Examples: Jake (rep) sees no Leadership folder or dashboard; Rachel sees Leadership
but not the QBR; Grace (Globex) sees the QBR but not the Acme Workspace.

## Run it

1. Create the database (safe to re-run):
   ```
   sqlcmd -S .\SQLEXPRESS -E -C -i Database\CreateMultiTenantDemoDb.sql
   ```
2. Copy `appsettings.sample.json` to `appsettings.json` and fill in your Dotnet Report tokens and the
   `MultiTenantDemo` connection string.
3. In Dotnet Report Setup, create a data connection with **Connection Key = `MultiTenantDemo`**, put its key in
   `dataconnectApiToken`, then load and save the `dbo` tables/views (not `app.*`) and add the joins on
   `CustomerId`, `SalesRepId`, `TenantId`, `OrderId` and `ProductId`.
4. Build the client libraries once, then run:
   ```
   npm install
   npx gulp scripts
   dotnet run
   ```


## Demo script

1. **Sign in** as `sarah.chen@dotnetreport.demo`. *My Access* shows the real `GetSettings()` output:
   `ClientId`, roles, `DataFilters`, and the SQL the engine appends.
2. **Users & Access**: the org chart, each user's roles, tenants and DataFilters, and how many orders each can see.
3. **Dashboards → Sales Overview** and **Reports**: every folder, all 19 reps across 5 tenants.
4. **Impersonate Rachel Kim**: Sales by Rep drops to 3 rows (Rachel, Jake, Emma). The Operations & Support folder
   is gone, and the QBR isn't in Leadership.
5. **Impersonate Jake Wilson** (from Sarah or Rachel): only his own rows, no Leadership folder or dashboard.
6. **Impersonate Linda Park**: 9 reps across two tenants, plus the QBR shared with her by name. Switch the tenant
   picker to Globex and everything narrows to her Globex team.
7. **Impersonate Grace Lee** (Globex): no Acme Workspace, because that folder is restricted to the ACME tenant.
8. Show that **Setup** is only in Sarah's menu, and that a manager can only impersonate their own team.

## Files

| File | What it does |
|---|---|
| `Security/AccessProfile.cs` | The access rules: scope, tenants, hierarchy, ClientId, impersonation, and the DataFilters builder |
| `Security/DemoDirectory.cs` | Reads tenants, users and roles from the database (cached for 30s) |
| `Controllers/AccountController.cs` | Login, impersonate / stop, tenant switch |
| `Controllers/UsersController.cs`, `Views/Users` | Users & Access page |
| `Controllers/DotNetReportApiController.cs` | **The shipped-code change.** `GetSettings()`: claims to `ClientId`, `UserId`, roles, pick-lists and `DataFilters`. `GetUsersAndRoles()` always uses the code-supplied lists (the shared demo account's Users & Roles source is set to SQL) |

> Demo shortcut: passwords are unsalted SHA-256.
> Use your real identity provider in production. The pattern to copy is the claims and `GetSettings()`.
