#nullable enable
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;

namespace MultiTenantDemo.Security
{
    public record Tenant(int TenantId, string Code, string Name, string Industry, string Region);

    public record Role(int RoleId, string Name, string Description, string DataScope,
                       bool CanUseAdminMode, bool CanAccessSetup, bool CanImpersonate);

    public class DemoUser
    {
        public int UserId { get; init; }
        public string Email { get; init; } = "";
        public string FullName { get; init; } = "";
        public string Title { get; init; } = "";
        public int? HomeTenantId { get; init; }
        public int? ManagerUserId { get; init; }
        public bool IsActive { get; init; }
        public List<Role> Roles { get; } = new();
        public List<int> TenantIds { get; } = new();   // app.UserTenants
    }

    /// <summary>
    /// Everything the demo knows about tenants, users and roles, read from the MultiTenantDemo database.
    /// The data set is tiny, so it is loaded whole (and cached briefly); a real app would query what it needs.
    /// </summary>
    public class DemoDirectory
    {
        private readonly string _connectionString;

        public DemoDirectory(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MultiTenantDemo")
                ?? throw new InvalidOperationException("ConnectionStrings:MultiTenantDemo is not configured.");
        }

        public List<Tenant> Tenants { get; private set; } = new();
        public List<Role> Roles { get; private set; } = new();
        public List<DemoUser> Users { get; private set; } = new();

        public DemoUser? FindUser(int userId) => Users.FirstOrDefault(u => u.UserId == userId);
        public Tenant? FindTenant(int tenantId) => Tenants.FirstOrDefault(t => t.TenantId == tenantId);

        public async Task<DemoDirectory> LoadAsync()
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();

            Tenants = await ReadAsync(cn, "SELECT TenantId, TenantCode, TenantName, Industry, Region FROM dbo.Tenants ORDER BY TenantId",
                r => new Tenant(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));

            Roles = await ReadAsync(cn, "SELECT RoleId, RoleName, Description, DataScope, CanUseAdminMode, CanAccessSetup, CanImpersonate FROM app.Roles ORDER BY RoleId",
                r => new Role(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetBoolean(5), r.GetBoolean(6)));

            Users = await ReadAsync(cn, "SELECT UserId, Email, FullName, Title, HomeTenantId, ManagerUserId, IsActive FROM app.Users ORDER BY UserId",
                r => new DemoUser
                {
                    UserId = r.GetInt32(0),
                    Email = r.GetString(1),
                    FullName = r.GetString(2),
                    Title = r.GetString(3),
                    HomeTenantId = r.IsDBNull(4) ? null : r.GetInt32(4),
                    ManagerUserId = r.IsDBNull(5) ? null : r.GetInt32(5),
                    IsActive = r.GetBoolean(6)
                });

            var byId = Users.ToDictionary(u => u.UserId);
            foreach (var (userId, roleId) in await ReadAsync(cn, "SELECT UserId, RoleId FROM app.UserRoles", r => (r.GetInt32(0), r.GetInt32(1))))
                byId[userId].Roles.Add(Roles.First(x => x.RoleId == roleId));
            foreach (var (userId, tenantId) in await ReadAsync(cn, "SELECT UserId, TenantId FROM app.UserTenants ORDER BY TenantId", r => (r.GetInt32(0), r.GetInt32(1))))
                byId[userId].TenantIds.Add(tenantId);

            return this;
        }

        /// <summary>Demo-only password check (SHA2-256, matching the SQL script). Use a real identity provider in production.</summary>
        public async Task<DemoUser?> ValidateCredentialsAsync(string email, string password)
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT UserId, PasswordHash FROM app.Users WHERE Email = @email AND IsActive = 1", cn);
            cmd.Parameters.AddWithValue("@email", email ?? "");
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            var userId = r.GetInt32(0);
            var stored = (byte[])r[1];
            var supplied = SHA256.HashData(Encoding.Unicode.GetBytes(password ?? ""));   // nvarchar = UTF-16LE
            return CryptographicOperations.FixedTimeEquals(stored, supplied) ? FindUser(userId) : null;
        }

        /// <summary>The user and everyone below them in the reporting hierarchy.</summary>
        public List<int> GetTeam(int userId)
        {
            var team = new List<int> { userId };
            for (var i = 0; i < team.Count; i++)
                team.AddRange(Users.Where(u => u.ManagerUserId == team[i] && !team.Contains(u.UserId)).Select(u => u.UserId));
            return team;
        }

        /// <summary>Orders and revenue the given filter values let through - the same rule the report engine applies.</summary>
        public async Task<(int Orders, decimal Revenue)> CountVisibleAsync(IReadOnlyCollection<int> tenantIds, IReadOnlyCollection<int>? salesRepIds)
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(@"
                SELECT COUNT(DISTINCT o.OrderId), COALESCE(SUM(i.LineTotal), 0)
                FROM dbo.Orders o
                JOIN dbo.OrderItems i ON i.OrderId = o.OrderId
                WHERE o.TenantId IN (SELECT CAST(value AS int) FROM STRING_SPLIT(@tenants, ','))
                  AND (@reps IS NULL OR o.SalesRepId IN (SELECT CAST(value AS int) FROM STRING_SPLIT(@reps, ',')))", cn);
            cmd.Parameters.AddWithValue("@tenants", tenantIds.Count > 0 ? string.Join(",", tenantIds) : "-1");
            cmd.Parameters.AddWithValue("@reps", salesRepIds == null ? DBNull.Value : string.Join(",", salesRepIds.DefaultIfEmpty(-1)));
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            return (r.GetInt32(0), r.GetDecimal(1));
        }

        private static async Task<List<T>> ReadAsync<T>(SqlConnection cn, string sql, Func<SqlDataReader, T> map)
        {
            var list = new List<T>();
            await using var cmd = new SqlCommand(sql, cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(map(r));
            return list;
        }
    }

    /// <summary>Caches the loaded directory briefly so the many report API calls per page don't each hit the database.</summary>
    public class DemoDirectoryProvider
    {
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;

        public DemoDirectoryProvider(IConfiguration configuration, IMemoryCache cache)
        {
            _configuration = configuration;
            _cache = cache;
        }

        public async Task<DemoDirectory> GetAsync() =>
            (await _cache.GetOrCreateAsync("demo-directory", e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                return new DemoDirectory(_configuration).LoadAsync();
            }))!;

        public DemoDirectory Get() => GetAsync().GetAwaiter().GetResult();

        /// <summary>For GetSettings(): users the signed-in person may pick in Manage Access, as { id = email, text = name }.</summary>
        public List<dynamic> UsersVisibleTo(ClaimsPrincipal user)
        {
            var directory = Get();
            var me = int.TryParse(user.FindFirst(DemoClaims.UserId)?.Value, out var id) ? directory.FindUser(id) : null;
            if (me == null) return new List<dynamic>();

            var profile = new AccessProfile(directory, me, user.FindFirst(DemoClaims.ActiveTenant)?.Value);
            return directory.Users.Where(u => profile.CanSeeUser(directory, u))
                .Select(u => (dynamic)new { id = u.Email, text = u.FullName }).ToList();
        }

        /// <summary>For GetSettings(): every role name.</summary>
        public List<string> RoleNames() => Get().Roles.Select(r => r.Name).ToList();
    }
}
