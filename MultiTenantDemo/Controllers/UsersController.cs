#nullable enable
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiTenantDemo.Models;
using MultiTenantDemo.Security;

namespace MultiTenantDemo.Controllers
{
    /// <summary>Users, roles, tenant access and hierarchy - scoped to what the signed-in user may see.</summary>
    [Authorize]
    public class UsersController : Controller
    {
        private readonly DemoDirectoryProvider _directory;

        public UsersController(DemoDirectoryProvider directory)
        {
            _directory = directory;
        }

        public async Task<IActionResult> Index()
        {
            var directory = await _directory.GetAsync();
            var me = User.DemoProfile(directory);
            if (me == null) return RedirectToAction("Login", "Account");

            // Impersonation is decided by the real user; while impersonating, only "return" is offered
            var impersonating = User.HasClaim(c => c.Type == DemoClaims.ImpersonatorId);

            var rows = new List<UserRow>();
            foreach (var user in directory.Users.Where(u => me.CanSeeUser(directory, u)))
            {
                var profile = new AccessProfile(directory, user);
                var (orders, revenue) = await directory.CountVisibleAsync(profile.FilterTenantIds, profile.SalesRepIds);
                rows.Add(new UserRow
                {
                    User = user,
                    Profile = profile,
                    Manager = user.ManagerUserId is int m ? directory.FindUser(m) : null,
                    DirectReports = directory.Users.Count(u => u.ManagerUserId == user.UserId),
                    VisibleOrders = orders,
                    VisibleRevenue = revenue,
                    CanImpersonate = !impersonating && me.CanImpersonateUser(directory, user)
                });
            }

            var tenants = new List<TenantRow>();
            foreach (var tenant in me.AllowedTenants)
            {
                var (orders, revenue) = await directory.CountVisibleAsync(new[] { tenant.TenantId }, null);
                tenants.Add(new TenantRow
                {
                    Tenant = tenant,
                    Users = directory.Users.Count(u => u.TenantIds.Contains(tenant.TenantId)),
                    Orders = orders,
                    Revenue = revenue
                });
            }

            return View(new UsersPageModel
            {
                Me = me,
                Directory = directory,
                Rows = rows,
                Tenants = tenants,
                IsImpersonating = impersonating
            });
        }
    }
}
