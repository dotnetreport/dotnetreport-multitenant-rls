// Multi-tenant row-level security demo for Dotnet Report.
//
// Sign-in (AccountController) turns a user from the MultiTenantDemo database into claims:
// tenant ids, sales-rep ids from the reporting hierarchy, roles and capability flags.
// DotNetReportApiController.GetSettings() reads those claims on every report request and passes
// them to the engine as ClientId / UserId / CurrentUserRole / DataFilters.

using Microsoft.AspNetCore.Authentication.Cookies;
using MultiTenantDemo.Security;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

services.AddControllersWithViews();
services.AddMemoryCache();
services.AddSingleton<DemoDirectoryProvider>();

services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/AccessDenied";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });

services.AddAuthorization(o =>
{
    // /DotnetSetup (schema, data connections) is for the platform admin only
    o.AddPolicy("CanAccessSetup", p => p.RequireClaim(DemoClaims.CanAccessSetup, bool.TrueString));
});

var app = builder.Build();

// Scheduled report emails. Each schedule stores the DataFilters of the user who created it,
// so isolation holds when nobody is signed in.
// ReportBuilder.Web.Jobs.JobScheduler.Start();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
