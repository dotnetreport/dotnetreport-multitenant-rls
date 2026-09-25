# Multi-tenant reporting with row-level security in ASP.NET Core

How to give every customer of a multi-tenant SaaS app their own self-service reporting — with **row-level security enforced server-side**, so tenant A can never see tenant B's rows, even when users build their own reports.

Built on [Dotnet Report](https://dotnetreport.com), an embedded report builder for .NET. This repo contains only the files you change on top of a standard install (see the [quickstart repo](https://github.com/dotnetreport/dotnetreport-aspnetcore-quickstart) for the base setup).

> The Dotnet Report report-builder front-end is source-available on GitHub: https://github.com/dotnetreport/dotnetreport

> **Runnable demo:** [`MultiTenantDemo/`](MultiTenantDemo/README.md) is a complete app with a SQL script for tenants,
> users, roles, a reporting hierarchy and sales data, a login page, a Users & Access page with impersonation,
> and `GetSettings()` building DataFilters from the signed-in user.

## The problem

Self-service reporting and multi-tenancy pull against each other. Once users can pick any table and any column, "just add `WHERE TenantId = @tenant` to the query" stops being something a developer controls — the user is writing the query. You need the isolation to be applied **by the engine**, on every query, from context the user cannot change.

## How Dotnet Report handles it

Every request from the report builder goes through a single server-side method, `GetSettings()`, in `DotNetReportApiController`. Whatever you put in there is sent to the reporting engine with every call — and the engine applies it. Three properties do the work:

| Property | Purpose |
|---|---|
| `ClientId` | The tenant. Scopes **reports, folders and dashboards** to that tenant. |
| `UserId` / `CurrentUserRole` | The user and their roles. Scopes ownership, sharing and role-based access. |
| `DataFilters` | **Row-level security.** A filter the engine appends to every generated SQL statement. |

`DataFilters` is an anonymous object whose property names are column names and whose values are comma-separated allowed values. This:

```csharp
settings.DataFilters = new { TenantId = "42" };
```

makes every report query end with, in effect:

```sql
... AND [Table].[TenantId] IN (42)
```

for every table that has a `TenantId` column. To target a specific table, use the `Table__Column` form (double underscore):

```csharp
settings.DataFilters = new { Orders__TenantId = "42", Customers__TenantId = "42" };
```

You can combine several filters, and a value can list several ids (`"42,43"`) for users who legitimately span tenants.

## The change: drive it from the user's claims

Never hard-code these values. Read them from the authenticated user's claims so the engine enforces isolation from context the user cannot forge. The full method is in [`DotNetReportApiController.GetSettings.cs`](DotNetReportApiController.GetSettings.cs); the important part:

```csharp
var user = User as ClaimsPrincipal;

// Tenant (multi-tenant client) — from a claim you issue at login
var tenantId = user?.FindFirst("tenant_id")?.Value ?? string.Empty;

settings.ClientId = tenantId;                                            // scopes reports/folders/dashboards
settings.UserId   = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value    // scopes ownership & sharing
                    ?? string.Empty;
settings.UserName = user?.Identity?.Name ?? string.Empty;
settings.CurrentUserRole = user?.Claims
    .Where(c => c.Type == ClaimTypes.Role)
    .Select(c => c.Value)
    .ToList() ?? new List<string>();

// Row-level security: the engine appends AND [Table].[TenantId] IN (...) to every query
settings.DataFilters = new { TenantId = tenantId };
```

Issue the `tenant_id` claim wherever you sign users in — see [`Program.cs`](Program.cs) for a cookie-auth example that adds it alongside the standard identifier and role claims.

## Why this is safe

- The filter is applied **server-side by the reporting engine**, not in the browser and not by trusting user input.
- It applies to **every** query the builder generates — ad hoc reports, dashboards, drill-downs, exports and scheduled emails alike (scheduled reports carry their own saved `DataFilters`, so isolation holds when nobody is logged in).
- `ClientId` separately keeps each tenant's saved reports and folders out of the others' view.

## Going further

- **Per-user restrictions beyond tenant.** Add more filters from claims, e.g. `new { TenantId = tenantId, RegionId = regionIds }` for regional managers.
- **Role-based access to reports.** `CurrentUserRole` and `UserRoles` let you restrict which reports/folders a role can see; admin capabilities are gated by claims such as `AllowAdminMode` and `AllowSetupPageAccess`.
- **Cross-tenant admins.** Give a support role a claim listing several tenant ids and pass `"1,2,3"` — the engine expands it to an `IN` list.

## Setup checklist

1. Install and configure Dotnet Report ([quickstart](https://github.com/dotnetreport/dotnetreport-aspnetcore-quickstart)).
2. Make sure your tenant-scoped tables actually have a tenant column (e.g. `TenantId`).
3. Issue a `tenant_id` claim (and the standard `NameIdentifier`/`Role` claims) at login.
4. Replace the `ClientId` / `UserId` / `CurrentUserRole` / `DataFilters` lines in `GetSettings()` with the claims-driven version above.
5. Test with two users in two tenants and confirm neither can see the other's rows or saved reports.

## Related

- Global data filters docs: https://dotnetreport.com/docs/#global-filters
- Users, roles & security docs: https://dotnetreport.com/docs

---

Maintained by the Dotnet Report team. Issues and PRs welcome.
