using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SuperAdmin.Data;

namespace SuperAdmin.Controllers;

/// <summary>
/// "New company": adds a company whose database has already been copied from an
/// existing company. Step 1 shows exactly what will be added (nothing written);
/// step 2 writes it after the owner confirms. Owners only.
/// </summary>
[Route("companies/new"), Authorize(Policy = Policies.Owner)]
public sealed class NewCompanyController : Controller
{
    private readonly NewCompanyStore _store;
    public NewCompanyController(NewCompanyStore store) => _store = store;

    public sealed class Form
    {
        public int SourceId { get; set; }
        public int? NewId { get; set; }
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string NewDatabase { get; set; } = "";
        public bool CopyMenus { get; set; } = true;
        public bool CopySettings { get; set; }
        public bool MakeCopy { get; set; }

        public NewCompanyStore.Request ToRequest() => new(SourceId, NewId, Name, Url, NewDatabase, CopyMenus, CopySettings, MakeCopy);
    }

    public sealed class PageModel
    {
        public Form Form { get; init; } = new();
        public IReadOnlyList<(int Id, string Name)> Sources { get; init; } = [];
        public NewCompanyStore.Plan? Plan { get; init; }
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int? from, CancellationToken ct) =>
        View(new PageModel
        {
            Form = new Form { SourceId = from ?? 0, NewId = await _store.SuggestIdAsync(ct) },
            Sources = await _store.SourcesAsync(ct),
        });

    /// <summary>Step 1: check everything and show what will be added. Writes nothing.</summary>
    [HttpPost("check")]
    public async Task<IActionResult> Check(Form form, CancellationToken ct) =>
        View("Index", new PageModel { Form = form, Sources = await _store.SourcesAsync(ct), Plan = await _store.PlanAsync(form.ToRequest(), ct) });

    /// <summary>Step 2: write it (re-checked; one transaction).</summary>
    [HttpPost("create")]
    public async Task<IActionResult> Create(Form form, CancellationToken ct)
    {
        var (plan, newId) = await _store.CreateAsync(form.ToRequest(), User.Identity?.Name ?? "?", ct);
        if (newId == 0)
            return View("Index", new PageModel { Form = form, Sources = await _store.SourcesAsync(ct), Plan = plan });

        TempData["Flash"] = $"Company {newId} “{form.Name.Trim()}” is added and its panels now open on database “{form.NewDatabase.Trim()}”"
                          + (plan.Copy is not null ? $" (copied by Super Admin; backup file left at {plan.Copy.BackupFile})" : "") + ". "
                          + $"The database still has {plan.SourceName}'s name, address and GST - change them below.";
        return Redirect($"/company/{newId}/details");
    }
}
