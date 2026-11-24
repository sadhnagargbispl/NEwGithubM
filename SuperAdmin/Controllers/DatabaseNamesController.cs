using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SuperAdmin.Data;

namespace SuperAdmin.Controllers;

/// <summary>
/// "Database names": rewrite the old company's database names inside the stored procedures,
/// views, functions and triggers of copied databases. Check first (rolled back), then Apply.
/// Owners only. The password is posted each time and never stored or written back to the page.
/// </summary>
[Route("tools/database-names"), Authorize(Policy = Policies.Owner)]
public sealed class DatabaseNamesController : Controller
{
    private readonly DatabaseNamesStore _store;
    private readonly WebsiteStore _websites;
    private readonly Db _db;

    public DatabaseNamesController(DatabaseNamesStore store, WebsiteStore websites, Db db)
    {
        _store = store;
        _websites = websites;
        _db = db;
    }

    public sealed class Form
    {
        public string Server { get; set; } = "";
        public string Login { get; set; } = "";
        public string Password { get; set; } = "";
        public string Databases { get; set; } = "";
        public string OldName { get; set; } = "";
        public string NewName { get; set; } = "";

        public DatabaseNamesStore.Request ToRequest() =>
            new(Server, Login, Password, DatabaseNamesStore.ParseDatabases(Databases), OldName, NewName);
    }

    public sealed class PageModel
    {
        public Form Form { get; init; } = new();
        public DatabaseNamesStore.Result? Result { get; init; }
        public bool WasApply { get; init; }
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(new PageModel { Form = new Form { Server = await DefaultServerAsync(ct) } });

    // Nothing written in code: the "SQL server address" saved in Super Admin (All companies → Websites),
    // else the server the master connection in web.config points at.
    private async Task<string> DefaultServerAsync(CancellationToken ct)
    {
        try
        {
            var (value, on) = await _websites.ServerAddressAsync(ct);
            if (on && !string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        catch (Microsoft.Data.SqlClient.SqlException) { /* setting not readable - fall back */ }
        return _db.Server;
    }

    [HttpPost("check")]
    public async Task<IActionResult> Check(Form form, CancellationToken ct)
    {
        var res = await _store.RunAsync(form.ToRequest(), apply: false, User.Identity?.Name ?? "?", ct);
        form.Password = "";
        return View("Index", new PageModel { Form = form, Result = res });
    }

    [HttpPost("apply")]
    public async Task<IActionResult> Apply(Form form, CancellationToken ct)
    {
        var res = await _store.RunAsync(form.ToRequest(), apply: true, (User.Identity?.Name ?? "?") + " (Super Admin)", ct);
        form.Password = "";
        return View("Index", new PageModel { Form = form, Result = res, WasApply = true });
    }
}
