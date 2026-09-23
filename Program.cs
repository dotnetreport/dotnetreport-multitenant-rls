// ASP.NET Core host showing how to issue the claims that GetSettings() reads.
//
// The report builder itself only needs: controllers-with-views, HttpClient,
// IHttpContextAccessor, session, and authentication. The important addition for
// multi-tenancy is the "tenant_id" claim issued at sign-in (see SignInAsync below).
// Plug this into whatever authentication you already use — ASP.NET Core Identity,
// OpenID Connect, etc. The only requirement is that the tenant ends up as a claim.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

services.AddControllersWithViews();
services.AddHttpClient();
services.AddHttpContextAccessor();
services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromHours(8);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
});

services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Home/Login";
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
    });

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

// Example sign-in that issues the claims GetSettings() relies on.
// In a real app this lives in your login action after you've verified credentials
// and looked up the user's tenant and roles from YOUR user store.
app.MapPost("/Home/Login", async (HttpContext http, string userId, string userName, string tenantId, string[] roles) =>
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, userId),   // -> settings.UserId
        new(ClaimTypes.Name, userName),           // -> settings.UserName
        new("tenant_id", tenantId),               // -> settings.ClientId + DataFilters (row-level security)
    };
    claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));   // -> settings.CurrentUserRole

    // Optional capability claims used by Dotnet Report:
    //   ClaimsStore.AllowAdminMode        -> can enter Admin mode in the builder
    //   ClaimsStore.AllowSetupPageAccess  -> can open /dotnetsetup
    // e.g. claims.Add(new Claim(ClaimsStore.AllowAdminMode, "true"));

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/dotnetreport");
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
