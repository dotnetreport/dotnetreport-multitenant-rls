// Claims-driven multi-tenant context + row-level security for Dotnet Report.
//
// This is the GetSettings() method from DotNetReportApiController (installed by the
// DotnetReport NuGet package) with the tenant/user/filter section replaced by values
// read from the authenticated user's claims. Everything the report builder does —
// ad hoc reports, dashboards, drill-downs, exports — flows through this method, and
// the reporting engine applies DataFilters to every generated SQL statement.
//
// Replace the corresponding lines in your installed controller with the block marked
// "Multi-tenant context". Leave the token/config lines as installed.

using System.Security.Claims;
using ReportBuilder.Web.Helper;
using ReportBuilder.Web.Models;

namespace ReportBuilder.Web.Controllers
{
    public partial class DotNetReportApiController
    {
        private DotNetReportSettings GetSettings()
        {
            DotNetReportHelper.dbtype = DbTypes.MS_SQL.ToDbString();

            var tokenConfig = DotNetReportHelper.StaticConfig;

            var settings = new DotNetReportSettings
            {
                ApiUrl = _configuration.GetValue<string>("dotNetReport:apiUrl"),
                AccountApiToken = tokenConfig.GetValue<string>("dotNetReport:accountApiToken"),
                DataConnectApiToken = tokenConfig.GetValue<string>("dotNetReport:dataconnectApiToken")
            };

            var appSettings = DotNetReportHelper.GetAppSettings();
            if (!string.IsNullOrEmpty(appSettings.backendApiUrl))
            {
                settings.ApiUrl = appSettings.backendApiUrl;
            }

            // ---------------------------------------------------------------
            // Multi-tenant context (read from claims — never hard-code these)
            // ---------------------------------------------------------------
            var user = User as ClaimsPrincipal;

            // The tenant this user belongs to. Issue this claim at login.
            var tenantId = user?.FindFirst("tenant_id")?.Value ?? string.Empty;

            // Scopes saved reports, folders and dashboards to the tenant.
            settings.ClientId = tenantId;

            // Scopes report ownership and sharing to the user.
            settings.UserId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            settings.UserName = user?.Identity?.Name ?? string.Empty;

            // Roles drive role-based access to reports/folders.
            var currentUserRoles = user?.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList() ?? new List<string>();
            settings.CurrentUserRole = currentUserRoles;

            // All roles known to the system (used to populate sharing/role pickers).
            // Session is the installed default; fall back to the user's own roles.
            var userRoles = SessionHelper.GetUserRoles(HttpContext);
            if (!userRoles.Any() && currentUserRoles.Any())
                userRoles = currentUserRoles;
            settings.UserRoles = userRoles;

            settings.Users = SessionHelper.GetUsers(HttpContext)?.Any() == true
                ? SessionHelper.GetUsers(HttpContext)
                : new List<dynamic>();

            // Only users with the AllowAdminMode claim can enter Admin mode.
            settings.CanUseAdminMode = ClaimsHelper.HasAnyRequiredClaim(user, ClaimsStore.AllowAdminMode);

            // Row-level security. The engine appends  AND [Table].[TenantId] IN (...)
            // to every generated query for any table that has a TenantId column.
            //
            //   settings.DataFilters = new { TenantId = tenantId };
            //
            // Target specific tables with the Table__Column form, and combine filters:
            //
            //   settings.DataFilters = new
            //   {
            //       Orders__TenantId    = tenantId,
            //       Customers__TenantId = tenantId,
            //       RegionId            = user?.FindFirst("region_ids")?.Value ?? ""   // e.g. "3,7"
            //   };
            settings.DataFilters = new { TenantId = tenantId };

            return settings;
        }
    }
}
