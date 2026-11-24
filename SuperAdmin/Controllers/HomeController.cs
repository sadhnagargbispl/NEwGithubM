using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SuperAdmin.Data;
using SuperAdmin.Models;

namespace SuperAdmin.Controllers;

public sealed class HomeController : Controller
{
    private readonly CompanyStore _companies;

    public HomeController(CompanyStore companies) => _companies = companies;

    /// <summary>All companies, with how much Super Admin has set for each.</summary>
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        var all = await _companies.ListAsync(ct);
        var list = string.IsNullOrWhiteSpace(q)
            ? all
            : all.Where(c => (c.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                          || (c.Url ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                          || c.Id.ToString() == q.Trim()).ToList();
        ViewBag.Query = q;
        ViewBag.Total = all.Count;
        return View(list);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
