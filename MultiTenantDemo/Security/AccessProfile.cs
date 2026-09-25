#nullable enable
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MultiTenantDemo.Security
{
    /// <summary>Claim types issued at sign-in and read back by DotNetReportApiController.GetSettings().</summary>
    public static class DemoClaims
    {
        public const string UserId = "user_id";
        public const string DataScope = "data_scope";
        public const string AllowedTenantIds = "allowed_tenant_ids";   // every tenant the user may switch to
        public const string AllowedTenantCodes = "allowed_tenant_codes"; // -> settings.ClientIds
        public const string ActiveTenant = "active_tenant";            // "*" = all allowed tenants, or one tenant id
        public const string TenantIds = "tenant_ids";                  // -> DataFilters.TenantId
        public const string SalesRepIds = "salesrep_ids";              // -> DataFilters.SalesRepId (absent = no rep restriction)
        public const string ClientId = "client_id";                    // -> settings.ClientId (tenant code, "" = shared workspace)
        public const string CanUseAdminMode = "can_admin_mode";
        public const string CanAccessSetup = "can_access_setup";
        public const string CanImpersonate = "can_impersonate";
        public const string ImpersonatorId = "impersonator_id";
        public const string ImpersonatorName = "impersonator_name";

        public const string AllTenants = "*";
    }

    /// <summary>
    /// What one user may see, worked out from their roles, tenant memberships and position in the hierarchy.
    /// This is the single place the access rules live; the result is issued as claims and the report
    /// engine enforces it through DataFilters.
    /// </summary>
    public class AccessProfile
    {
        private static readonly string[] ScopeOrder = { "AllTenants", "AssignedTenants", "Team", "Self" };

        public DemoUser User { get; }
        public string DataScope { get; }
        public List<Tenant> AllowedTenants { get; }
        public string ActiveTenant { get; }                 // "*" or tenant id
        public List<int> FilterTenantIds { get; }
        public List<int>? SalesRepIds { get; }              // null = every rep in the tenants
        public string ClientId { get; }
        public bool CanUseAdminMode { get; }
        public bool CanAccessSetup { get; }
        public bool CanImpersonate { get; }

        public AccessProfile(DemoDirectory directory, DemoUser user, string? activeTenant = null)
        {
            User = user;

            // A user with several roles gets the broadest scope among them.
            DataScope = user.Roles.Select(r => r.DataScope)
                .OrderBy(s => Array.IndexOf(ScopeOrder, s))
                .FirstOrDefault() ?? "Self";

            AllowedTenants = DataScope == "AllTenants"
                ? directory.Tenants.ToList()
                : directory.Tenants.Where(t => user.TenantIds.Contains(t.TenantId)).ToList();

            // Users in one tenant are pinned to it; users in several default to "all of mine".
            ActiveTenant = activeTenant != null
                           && (activeTenant == DemoClaims.AllTenants || AllowedTenants.Any(t => t.TenantId.ToString() == activeTenant))
                ? activeTenant
                : AllowedTenants.Count == 1 ? AllowedTenants[0].TenantId.ToString() : DemoClaims.AllTenants;

            var active = ActiveTenant == DemoClaims.AllTenants ? null : directory.FindTenant(int.Parse(ActiveTenant));
            FilterTenantIds = active != null ? new List<int> { active.TenantId } : AllowedTenants.Select(t => t.TenantId).ToList();

            SalesRepIds = DataScope switch
            {
                "Team" => directory.GetTeam(user.UserId),
                "Self" => new List<int> { user.UserId },
                _ => null
            };

            // ClientId scopes saved reports/folders/dashboards. Reports saved with an empty ClientId are shared
            // with every tenant, so only the platform admin's "all tenants" view uses it.
            ClientId = active?.Code
                       ?? (DataScope == "AllTenants" ? "" : directory.FindTenant(user.HomeTenantId ?? 0)?.Code ?? AllowedTenants.FirstOrDefault()?.Code ?? "");

            CanUseAdminMode = user.Roles.Any(r => r.CanUseAdminMode);
            CanAccessSetup = user.Roles.Any(r => r.CanAccessSetup);
            CanImpersonate = user.Roles.Any(r => r.CanImpersonate);
        }

        /// <summary>The Dotnet Report DataFilters for this profile (see ReportFilters.Build).</summary>
        public Dictionary<string, string> DataFilters => ReportFilters.Build(FilterTenantIds, SalesRepIds);

        /// <summary>
        /// Impersonation rules: platform admins can become anyone; tenant admins can become anyone
        /// whose tenants they administer; managers can become anyone in their team.
        /// </summary>
        public bool CanImpersonateUser(DemoDirectory directory, DemoUser target)
        {
            if (!CanImpersonate || target.UserId == User.UserId || !target.IsActive) return false;
            if (DataScope == "AllTenants") return true;

            var targetScope = new AccessProfile(directory, target);
            if (targetScope.DataScope == "AllTenants") return false;
            if (DataScope == "AssignedTenants")
                return targetScope.AllowedTenants.All(t => AllowedTenants.Any(a => a.TenantId == t.TenantId));
            return SalesRepIds?.Contains(target.UserId) == true;
        }

        /// <summary>Users this person may see on the Users page and pick in Dotnet Report's sharing dialogs.</summary>
        public bool CanSeeUser(DemoDirectory directory, DemoUser other)
        {
            if (DataScope == "AllTenants" || other.UserId == User.UserId) return true;
            if (other.Roles.Any(r => r.DataScope == "AllTenants")) return false;
            if (SalesRepIds != null) return SalesRepIds.Contains(other.UserId);
            return other.TenantIds.Any(id => AllowedTenants.Any(t => t.TenantId == id));
        }

        public ClaimsPrincipal ToPrincipal(DemoUser? impersonator = null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, User.Email),               // -> settings.UserId
                new(ClaimTypes.Name, User.FullName),                      // -> settings.UserName
                new(ClaimTypes.Email, User.Email),
                new(DemoClaims.UserId, User.UserId.ToString()),
                new(DemoClaims.DataScope, DataScope),
                new(DemoClaims.AllowedTenantIds, string.Join(",", AllowedTenants.Select(t => t.TenantId))),
                new(DemoClaims.AllowedTenantCodes, string.Join(",", AllowedTenants.Select(t => t.Code))),
                new(DemoClaims.ActiveTenant, ActiveTenant),
                new(DemoClaims.TenantIds, string.Join(",", FilterTenantIds)),
                new(DemoClaims.ClientId, ClientId),
                new(DemoClaims.CanUseAdminMode, CanUseAdminMode.ToString()),
                new(DemoClaims.CanAccessSetup, CanAccessSetup.ToString()),
                new(DemoClaims.CanImpersonate, CanImpersonate.ToString()),
            };
            if (SalesRepIds != null)
                claims.Add(new(DemoClaims.SalesRepIds, string.Join(",", SalesRepIds)));
            claims.AddRange(User.Roles.Select(r => new Claim(ClaimTypes.Role, r.Name)));   // -> settings.CurrentUserRole
            if (impersonator != null)
            {
                claims.Add(new(DemoClaims.ImpersonatorId, impersonator.UserId.ToString()));
                claims.Add(new(DemoClaims.ImpersonatorName, impersonator.FullName));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        }
    }

    public static class ReportFilters
    {
        /// <summary>
        /// Dotnet Report DataFilters: each key is a column name, each value the allowed ids. The engine appends
        ///   AND [Table].[TenantId] IN (...) AND [Table].[SalesRepId] IN (...)
        /// to every query on a table that has those columns. Fails closed: no tenants means no rows.
        /// </summary>
        public static Dictionary<string, string> Build(IEnumerable<int>? tenantIds, IEnumerable<int>? salesRepIds)
        {
            var tenants = tenantIds?.ToList() ?? new List<int>();
            var filters = new Dictionary<string, string>
            {
                ["TenantId"] = tenants.Count > 0 ? string.Join(",", tenants) : "-1"
            };
            if (salesRepIds != null)
                filters["SalesRepId"] = string.Join(",", salesRepIds.DefaultIfEmpty(-1));
            return filters;
        }

        /// <summary>The WHERE fragment the engine produces, for display on the demo pages.</summary>
        public static string ToSqlPreview(IDictionary<string, string> filters) =>
            string.Join("\n", filters.Select(f => $"AND [Table].[{f.Key}] IN ({f.Value})"));
    }
}
