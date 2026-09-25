#nullable enable
using System.Security.Claims;
using MultiTenantDemo.Security;

namespace MultiTenantDemo.Models;

public class UserRow
{
    public required DemoUser User { get; init; }
    public required AccessProfile Profile { get; init; }
    public DemoUser? Manager { get; init; }
    public int DirectReports { get; init; }
    public int VisibleOrders { get; init; }
    public decimal VisibleRevenue { get; init; }
    public bool CanImpersonate { get; init; }
}

public class TenantRow
{
    public required Tenant Tenant { get; init; }
    public int Users { get; init; }
    public int Orders { get; init; }
    public decimal Revenue { get; init; }
}

public class UsersPageModel
{
    public required AccessProfile Me { get; init; }
    public required DemoDirectory Directory { get; init; }
    public required List<UserRow> Rows { get; init; }
    public required List<TenantRow> Tenants { get; init; }
    public bool IsImpersonating { get; init; }

    public UserRow? Row(int userId) => Rows.FirstOrDefault(r => r.User.UserId == userId);

    /// <summary>Visible users whose manager is not visible (or who have none) start a branch of the org chart.</summary>
    public IEnumerable<UserRow> Roots => Rows.Where(r => r.User.ManagerUserId == null || Row(r.User.ManagerUserId.Value) == null);

    public IEnumerable<UserRow> ReportsOf(int userId) => Rows.Where(r => r.User.ManagerUserId == userId);
}

public class HomePageModel
{
    public required AccessProfile Me { get; init; }
    public required Dictionary<string, string> DataFilters { get; init; }   // exactly what GetSettings() sends
    public required string ClientId { get; init; }
    public string? ImpersonatorName { get; init; }
}

public static class ClaimsPrincipalExtensions
{
    public static int? DemoUserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst(DemoClaims.UserId)?.Value, out var id) ? id : null;

    /// <summary>Rebuilds the signed-in user's access profile, honouring the tenant they switched to.</summary>
    public static AccessProfile? DemoProfile(this ClaimsPrincipal user, DemoDirectory directory)
    {
        var me = user.DemoUserId() is int id ? directory.FindUser(id) : null;
        return me == null ? null : new AccessProfile(directory, me, user.FindFirst(DemoClaims.ActiveTenant)?.Value);
    }
}
