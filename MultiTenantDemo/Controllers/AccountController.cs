#nullable enable
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiTenantDemo.Security;

namespace MultiTenantDemo.Controllers
{
    public class AccountController : Controller
    {
        private readonly DemoDirectoryProvider _directory;

        public AccountController(DemoDirectoryProvider directory)
        {
            _directory = directory;
        }

        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
        {
            var directory = await _directory.GetAsync();
            var user = await directory.ValidateCredentialsAsync(email, password);
            if (user == null)
            {
                ViewBag.ReturnUrl = returnUrl;
                ViewBag.Error = "Invalid email or password.";
                ViewBag.Email = email;
                return View();
            }

            await SignInAsync(directory, user);
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        /// <summary>Sign in as another user, keeping who started it so they can switch back.</summary>
        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Impersonate(int userId, string? returnUrl = null)
        {
            if (User.HasClaim(c => c.Type == DemoClaims.ImpersonatorId))
                return BadRequest("Stop impersonating before impersonating someone else.");

            var directory = await _directory.GetAsync();
            var actor = CurrentUser(directory);
            var target = directory.FindUser(userId);
            if (actor == null || target == null) return NotFound();

            // Checked against the real user's profile, never against anything posted by the browser
            if (!new AccessProfile(directory, actor).CanImpersonateUser(directory, target))
                return Forbid();

            await SignInAsync(directory, target, impersonator: actor);
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        }

        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> StopImpersonating()
        {
            var directory = await _directory.GetAsync();
            var original = int.TryParse(User.FindFirst(DemoClaims.ImpersonatorId)?.Value, out var id) ? directory.FindUser(id) : null;
            if (original == null) return LocalRedirect("/");

            await SignInAsync(directory, original);
            return RedirectToAction("Index", "Users");
        }

        /// <summary>Users with several tenants choose one tenant, or all of theirs, as the active scope.</summary>
        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> SwitchTenant(string tenant, string? returnUrl = null)
        {
            var directory = await _directory.GetAsync();
            var user = CurrentUser(directory);
            if (user == null) return RedirectToAction(nameof(Login));

            var impersonator = int.TryParse(User.FindFirst(DemoClaims.ImpersonatorId)?.Value, out var id) ? directory.FindUser(id) : null;
            await SignInAsync(directory, user, tenant, impersonator);   // AccessProfile ignores tenants the user doesn't have
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        }

        [AllowAnonymous]
        public IActionResult AccessDenied() => View();

        private DemoUser? CurrentUser(DemoDirectory directory) =>
            int.TryParse(User.FindFirst(DemoClaims.UserId)?.Value, out var id) ? directory.FindUser(id) : null;

        private Task SignInAsync(DemoDirectory directory, DemoUser user, string? activeTenant = null, DemoUser? impersonator = null) =>
            HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new AccessProfile(directory, user, activeTenant).ToPrincipal(impersonator));
    }
}
