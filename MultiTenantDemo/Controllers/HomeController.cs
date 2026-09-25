#nullable enable
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiTenantDemo.Models;
using MultiTenantDemo.Security;
using ReportBuilder.Web.Controllers;

namespace MultiTenantDemo.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly DemoDirectoryProvider _directory;
    private readonly IConfiguration _configuration;

    public HomeController(DemoDirectoryProvider directory, IConfiguration configuration)
    {
        _directory = directory;
        _configuration = configuration;
    }

    /// <summary>"My access": who you are and exactly what the report engine will filter on.</summary>
    public async Task<IActionResult> Index()
    {
        var directory = await _directory.GetAsync();
        var me = User.DemoProfile(directory);
        if (me == null) return RedirectToAction("Login", "Account");

        // Show exactly what the report engine receives by calling the real GetSettings()
        var settings = new DotNetReportApiController(_configuration) { ControllerContext = ControllerContext }.GetSettings();

        return View(new HomePageModel
        {
            Me = me,
            DataFilters = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                System.Text.Json.JsonSerializer.Serialize(settings.DataFilters))!,
            ClientId = settings.ClientId,
            ImpersonatorName = User.FindFirst(DemoClaims.ImpersonatorName)?.Value
        });
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
