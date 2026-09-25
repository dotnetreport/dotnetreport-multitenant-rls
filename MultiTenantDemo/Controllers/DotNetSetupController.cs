using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ReportBuilder.Web.Controllers
{
    [Authorize(Policy = "CanAccessSetup")] // multi-tenant demo: platform admin only
    public class DotNetSetupController : Controller
    {
        public async Task<IActionResult> Index(string databaseApiKey = "")
        {
            return View();
        }
    }
}