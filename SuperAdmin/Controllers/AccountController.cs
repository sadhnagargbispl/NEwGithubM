using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SuperAdmin.Data;
using SuperAdmin.Models;

namespace SuperAdmin.Controllers;

[AllowAnonymous]
[Route("account")]
public sealed class AccountController : Controller
{
    private readonly UserStore _users;
    private readonly IConfiguration _config;

    public AccountController(UserStore users, IConfiguration config)
    {
        _users = users;
        _config = config;
    }

    [HttpGet("login")]
    public async Task<IActionResult> Login(string? returnUrl, CancellationToken ct)
    {
        if (!await _users.TablesExistAsync(ct)) return View("NotReady");
        if (await _users.CountAsync(ct) == 0) return RedirectToAction(nameof(Setup));
        return View(new LoginModel { ReturnUrl = returnUrl });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginModel m, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(m);

        var (result, user) = await _users.SignInAsync(m.UserName.Trim(), m.Password,
            HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), ct);

        switch (result)
        {
            case UserStore.SignInResult.Ok:
                await SignInCookieAsync(user!);
                return LocalRedirect(Url.IsLocalUrl(m.ReturnUrl) ? m.ReturnUrl! : "/");
            case UserStore.SignInResult.Locked:
                ModelState.AddModelError("", $"Too many wrong passwords. Try again in {UserStore.LockFor.TotalMinutes:0} minutes.");
                break;
            default:
                // Same message for unknown user, wrong password and switched-off user.
                ModelState.AddModelError("", "Wrong user name or password.");
                break;
        }
        m.Password = "";
        return View(m);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("denied")]
    public IActionResult Denied() => View();

    // ---- first-time setup: create the first owner ----------------------------

    [HttpGet("setup")]
    public async Task<IActionResult> Setup(CancellationToken ct)
    {
        if (!await _users.TablesExistAsync(ct)) return View("NotReady");
        if (await _users.CountAsync(ct) > 0) return RedirectToAction(nameof(Login));
        return View(new SetupModel { KeyConfigured = !string.IsNullOrWhiteSpace(_config["Setup:Key"]) });
    }

    [HttpPost("setup")]
    public async Task<IActionResult> Setup(SetupModel m, CancellationToken ct)
    {
        if (!await _users.TablesExistAsync(ct)) return View("NotReady");
        if (await _users.CountAsync(ct) > 0) return RedirectToAction(nameof(Login));

        var key = _config["Setup:Key"];
        m.KeyConfigured = !string.IsNullOrWhiteSpace(key);
        if (!m.KeyConfigured || !FixedEquals(m.SetupKey ?? "", key!))
            ModelState.AddModelError(nameof(m.SetupKey), "That setup key is not right.");
        if (m.Password != m.ConfirmPassword)
            ModelState.AddModelError(nameof(m.ConfirmPassword), "The two passwords are not the same.");
        if (!ModelState.IsValid) { m.Password = m.ConfirmPassword = ""; return View(m); }

        await _users.CreateAsync(m.UserName.Trim(), m.FullName.Trim(), m.Email?.Trim(), m.Mobile?.Trim(),
                                 Roles.Owner, m.Password, "first-time setup", ct);
        TempData["Flash"] = "Owner account created. Please sign in.";
        return RedirectToAction(nameof(Login));
    }

    private async Task SignInCookieAsync(UserStore.SaUser u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.UserID.ToString()),
            new(ClaimTypes.Name, u.UserName),
            new("full_name", u.FullName),
            new(ClaimTypes.Role, u.Role),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    }

    private static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
