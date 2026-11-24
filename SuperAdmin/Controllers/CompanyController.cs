using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SuperAdmin.Data;
using SuperAdmin.Models;

namespace SuperAdmin.Controllers;

/// <summary>
/// One company's settings for the panels (or company 0 = "All companies", the
/// default every company inherits). Tabs: words, value names, rules, change log.
/// </summary>
[Route("company/{id:int}")]
public sealed class CompanyController : Controller
{
    private static readonly string[] Tabs = ["details", "websites", "words", "units", "rules", "forms", "branding", "menus", "dashboard", "panel", "changes"];

    private readonly CompanyStore _companies;
    private readonly ConfigStore _config;
    private readonly MenuStore _menus;
    private readonly SettingsCatalog _catalog;
    private readonly CompanyDetailsStore _details;
    private readonly WebsiteStore _websites;

    public CompanyController(CompanyStore companies, ConfigStore config, MenuStore menus, SettingsCatalog catalog, CompanyDetailsStore details, WebsiteStore websites)
    {
        _companies = companies;
        _config = config;
        _menus = menus;
        _catalog = catalog;
        _details = details;
        _websites = websites;
    }

    private string Actor => $"{User.Identity?.Name} (Super Admin)";

    [HttpGet("{tab?}")]
    public async Task<IActionResult> Index(int id, string? tab, string? form, string? panel, string? page, string? q, CancellationToken ct)
    {
        var company = await _companies.GetAsync(id, ct);
        if (company is null) return NotFound();
        tab = Tabs.Contains(tab) ? tab! : "words";

        // Remember the company being edited, so the side menu keeps pointing at it.
        Response.Cookies.Append(CurrentCompany.Cookie, id.ToString(), new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
        });

        var vm = new CompanyPageModel { Company = company, Tab = tab };
        switch (tab)
        {
            case "details":
                if (id != 0) vm.Details = await _details.GetAsync(id, ct);   // each company has its own row
                if (TempData["DetailsTyped"] is string typedJson)
                    vm.DetailsTyped = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(typedJson);
                break;
            case "websites":
                if (id != 0) vm.Websites = await _websites.GetAsync(id, ct);   // addresses belong to one company
                else vm.ServerAddress = await _websites.ServerAddressAsync(ct); // SQL server address, for every company
                break;
            case "words": vm.Layer = await _config.LabelsAsync(id, ct); break;
            case "units": vm.Layer = await _config.UnitsAsync(id, ct); break;
            case "rules": vm.Layer = await _config.RulesAsync(id, ct); break;
            case "forms":
                vm.Form = Catalog.Forms.FirstOrDefault(f => f.Key == form) ?? Catalog.Forms[0];
                vm.Fields = await _config.FormFieldsAsync(id, vm.Form.Key, ct);
                break;
            case "branding": (vm.Brand, vm.BrandDefault) = await _config.BrandingAsync(id, ct); break;
            case "menus":
                vm.MenuPanel = MenuStore.Find(panel) ?? MenuStore.Panels[0];
                if (id != 0)   // menus live per company; "All companies" has none
                {
                    vm.Menu = await _menus.CompanyMenuAsync(vm.MenuPanel, id, ct);
                    vm.MenuMissing = await _menus.MissingAsync(vm.MenuPanel, id, ct);
                }
                break;
            case "dashboard":
                vm.Layer = await _config.RulesAsync(id, ct);        // dash.<block>.show / .width
                vm.DashLabels = await _config.LabelsAsync(id, ct);  // dash.<block>.title
                break;
            case "panel":
            {
                var all = _catalog.All;
                vm.SettingsPanel = SettingsCatalog.PanelNames.Any(p => p.Key == panel) ? panel! : "member";
                var inPanel = all.Where(s => s.Panel == vm.SettingsPanel).ToList();
                vm.SettingsPages = inPanel.GroupBy(s => s.Page, StringComparer.OrdinalIgnoreCase)
                                          .Select(g => (g.Key, g.Count())).OrderBy(p => p.Key).ToList();
                vm.SettingsPage = page;
                vm.SettingsQuery = q;
                vm.SettingsCountByPanel = SettingsCatalog.PanelNames.ToDictionary(p => p.Key, p => all.Count(s => s.Panel == p.Key));
                IEnumerable<SettingsCatalog.Setting> shown = inPanel;
                if (!string.IsNullOrWhiteSpace(page)) shown = shown.Where(s => string.Equals(s.Page, page, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(q))
                    shown = shown.Where(s => s.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                                          || s.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
                                          || (s.Today ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
                vm.Settings = shown.ToList();
                vm.Layer = await _config.RulesAsync(id, ct);       // switch / text / number values
                vm.DashLabels = await _config.LabelsAsync(id, ct); // label values
                break;
            }
            case "changes": vm.Changes = await _config.ChangesAsync(id, 200, ct); break;
        }
        return View(vm);
    }

    // ---- company details (M_CompanyMaster in the company's database) -------

    private static readonly System.Text.RegularExpressions.Regex Gst =
        new(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9A-Z]Z[0-9A-Z]$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex Pan =
        new(@"^[A-Z]{5}[0-9]{4}[A-Z]$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex Phone =
        new(@"^[0-9+\-\s/,]{6,40}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    [HttpPost("details/save"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> SaveDetails(int id, decimal rowId, CancellationToken ct)
    {
        if (id == 0) return BadRequest();
        var company = await _companies.GetAsync(id, ct);
        if (company is null) return NotFound();
        var current = await _details.GetAsync(id, ct);
        if (!current.Reachable) return Back(id, "details", current.Problem ?? "The company's database could not be opened.", error: true);

        var typed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var f in CompanyDetailsStore.Fields.Where(f => current.Values.ContainsKey(f.Column)))
        {
            var v = (Request.Form[f.Column].ToString() ?? "").Trim();
            if (f.Kind is "gst" or "gstish" or "pan") v = v.ToUpperInvariant();
            if (f.Kind != "multiline") v = System.Text.RegularExpressions.Regex.Replace(v, @"\s+", " ");
            typed[f.Column] = v;

            var unchanged = string.Equals(current.Values[f.Column] ?? "", v, StringComparison.Ordinal);
            var max = current.MaxLength[f.Column];
            if (max > 0 && v.Length > max) { errors.Add($"{f.Label}: up to {max} characters."); continue; }
            if (unchanged || v.Length == 0)
            {
                if (f.Kind == "state" && v.Length == 0 && !unchanged) errors.Add("State: choose a state.");
                continue;   // old data is kept as it is, even if it is not in today's format
            }
            switch (f.Kind)
            {
                case "gst" when !Gst.IsMatch(v): errors.Add($"{f.Label}: not a valid GST number (15 characters, e.g. 08ABCDE1234F1Z5)."); break;
                case "pan" when !Pan.IsMatch(v): errors.Add($"{f.Label}: not a valid PAN (e.g. ABCDE1234F)."); break;
                case "phone" when !Phone.IsMatch(v): errors.Add($"{f.Label}: digits only (spaces, +, - allowed)."); break;
                case "email" when !System.Net.Mail.MailAddress.TryCreate(v, out _): errors.Add($"{f.Label}: not a valid email address."); break;
                case "state" when !int.TryParse(v, out var code) || (current.States.Count > 0 && current.States.All(s => s.Code != code)):
                    errors.Add("State: choose a state from the list."); break;
            }
        }
        if (errors.Count > 0)
        {
            TempData["FlashError"] = "Not saved: " + string.Join(" ", errors);
            TempData["DetailsTyped"] = System.Text.Json.JsonSerializer.Serialize(typed);
            return RedirectToAction(nameof(Index), new { id, tab = "details" });
        }

        try
        {
            var changes = await _details.SaveAsync(id, rowId, typed, Actor, ct);
            if (changes.Count == 0) return Back(id, "details", "Nothing changed.");
            var names = changes.Select(ch => CompanyDetailsStore.Fields.First(f => f.Column == ch.Column).Label);
            return Back(id, "details", $"Saved in {company.Name}'s database: {string.Join(", ", names)}. The panels show it on their next page.");
        }
        catch (Exception ex)
        {
            TempData["DetailsTyped"] = System.Text.Json.JsonSerializer.Serialize(typed);
            return Back(id, "details", "Not saved: " + ex.Message, error: true);
        }
    }

    // ---- words -------------------------------------------------------------

    [HttpPost("words/save"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> SaveWord(int id, string key, string? text, CancellationToken ct)
    {
        var word = Catalog.Words.FirstOrDefault(w => w.Key == key);
        text = text?.Trim();
        if (word is null) return BadRequest();
        if (string.IsNullOrEmpty(text) || text.Length > 500) return Back(id, "words", "Give the word some text (up to 500 characters).", error: true);
        await _config.SetLabelAsync(id, key, text, Actor, ct);
        return Back(id, "words", $"Saved: {word.Today} → {text}");
    }

    [HttpPost("words/reset"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> ResetWord(int id, string key, CancellationToken ct)
    {
        if (Catalog.Words.All(w => w.Key != key)) return BadRequest();
        await _config.ResetLabelAsync(id, key, Actor, ct);
        return Back(id, "words", "Reset - the panels show their own word again.");
    }

    // ---- value units -------------------------------------------------------

    [HttpPost("units/save"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> SaveUnit(int id, string code, string? name, bool enabled, int sort, CancellationToken ct)
    {
        if (Catalog.Units.All(u => u.Code != code)) return BadRequest();
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 60) return Back(id, "units", "Give the unit a name (up to 60 characters).", error: true);
        await _config.SetUnitAsync(id, code, name, enabled, Math.Clamp(sort, 0, 999), Actor, ct);
        return Back(id, "units", $"Saved: {code} → {name}");
    }

    [HttpPost("units/reset"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> ResetUnit(int id, string code, CancellationToken ct)
    {
        if (Catalog.Units.All(u => u.Code != code)) return BadRequest();
        await _config.ResetUnitAsync(id, code, Actor, ct);
        return Back(id, "units", $"Reset - {code} is shown as today.");
    }

    // ---- rules -------------------------------------------------------------

    [HttpPost("rules/save"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> SaveRule(int id, string key, string? value, string? state, CancellationToken ct)
    {
        var rule = Catalog.Rules.FirstOrDefault(r => r.Key == key);
        if (rule is null) return BadRequest();

        switch (rule.Kind)
        {
            case Catalog.RuleKind.Switch:
                if (state is not ("on" or "off")) return Back(id, "rules", "Choose On or Off.", error: true);
                await _config.SetRuleAsync(id, key, state == "on" ? "1" : "0", state == "on", Actor, ct);
                return Back(id, "rules", $"Saved: {rule.Label} → {(state == "on" ? "On" : "Off")}");
            case Catalog.RuleKind.Number:
                value = value?.Trim();
                if (!decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n) || n < 0)
                    return Back(id, "rules", "Enter a number (0 or more).", error: true);
                await _config.SetRuleAsync(id, key, n.ToString(System.Globalization.CultureInfo.InvariantCulture), true, Actor, ct);
                return Back(id, "rules", $"Saved: {rule.Label} → {n}");
            default:
                value = value?.Trim();
                if (string.IsNullOrEmpty(value) || value.Length > 1000) return Back(id, "rules", "Enter a value (up to 1000 characters).", error: true);
                await _config.SetRuleAsync(id, key, value, true, Actor, ct);
                return Back(id, "rules", $"Saved: {rule.Label}");
        }
    }

    [HttpPost("rules/reset"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> ResetRule(int id, string key, CancellationToken ct)
    {
        if (Catalog.Rules.All(r => r.Key != key)) return BadRequest();
        await _config.ResetRuleAsync(id, key, Actor, ct);
        return Back(id, "rules", "Reset - the panels follow today's rule again.");
    }

    // ---- form fields -------------------------------------------------------

    public sealed class FieldInput
    {
        public string Form { get; set; } = "";
        public string Field { get; set; } = "";
        public string? Show { get; set; }       // "" = as today | show | hide
        public string? Required { get; set; }   // "" | yes | no
        public string? ReadOnly { get; set; }   // "" | yes | no
        public string? Label { get; set; }
        public string? Placeholder { get; set; }
        public string? Help { get; set; }
        public string? Min { get; set; }
        public string? Max { get; set; }
        public string? Pattern { get; set; }
        public string? Order { get; set; }
    }

    [HttpPost("forms/save"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> SaveField(int id, FieldInput m, CancellationToken ct)
    {
        var form = Catalog.Forms.FirstOrDefault(f => f.Key == m.Form);
        var field = form?.Fields.FirstOrDefault(f => f.Key == m.Field);
        if (form is null || field is null) return BadRequest();

        string? Err(string msg) => msg;
        static bool? Tri(string? v, string yes, string no) => v == yes ? true : v == no ? false : null;
        static string? Text(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

        var show = Tri(m.Show, "show", "hide");
        var error =
            show == false && !field.CanHide ? Err($"“{field.Today}” cannot be hidden - the form needs it. You can still rename it.") :
            Text(m.Label)?.Length > 200 ? Err("Label: up to 200 characters.") :
            Text(m.Placeholder)?.Length > 200 ? Err("Hint: up to 200 characters.") :
            Text(m.Help)?.Length > 500 ? Err("Help text: up to 500 characters.") :
            null;

        int? Num(string? v, string name, int max)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            if (!int.TryParse(v.Trim(), out var n) || n < 0 || n > max) { error ??= $"{name}: a whole number from 0 to {max}."; return null; }
            return n;
        }
        var min = Num(m.Min, "Min length", 1000);
        var max = Num(m.Max, "Max length", 1000);
        var order = Num(m.Order, "Order", 999);
        if (min is not null && max is not null && min > max) error ??= "Min length cannot be more than max length.";

        var pattern = Text(m.Pattern);
        if (pattern is not null)
        {
            if (pattern.Length > 300) error ??= "Pattern: up to 300 characters.";
            else
            {
                try { _ = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(200)); }
                catch (ArgumentException) { error ??= "That pattern is not a valid regular expression."; }
            }
        }
        if (error is not null) return Back(id, "forms", error, error: true, form: form.Key);

        var rule = new ConfigStore.FieldRule
        {
            FieldKey = field.Key,
            IsVisible = show,
            IsRequired = Tri(m.Required, "yes", "no"),
            IsReadOnly = Tri(m.ReadOnly, "yes", "no"),
            LabelText = Text(m.Label),
            Placeholder = Text(m.Placeholder),
            HelpText = Text(m.Help),
            MinLength = min,
            MaxLength = max,
            Pattern = pattern,
            SortOrder = order,
        };
        await _config.SetFormFieldAsync(id, form.Key, rule, Actor, ct);
        return Back(id, "forms", rule.IsEmpty ? $"“{field.Today}” is back to how it is today." : $"Saved: {field.Today} → {rule.Describe()}", form: form.Key);
    }

    [HttpPost("forms/reset"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> ResetField(int id, string form, string field, CancellationToken ct)
    {
        var f = Catalog.Forms.FirstOrDefault(x => x.Key == form);
        var fld = f?.Fields.FirstOrDefault(x => x.Key == field);
        if (f is null || fld is null) return BadRequest();
        await _config.ResetFormFieldAsync(id, f.Key, fld.Key, Actor, ct);
        return Back(id, "forms", $"Reset - “{fld.Today}” is back to how it is today.", form: f.Key);
    }

    // ---- branding ----------------------------------------------------------

    public sealed class BrandingInput
    {
        public string? Logo { get; set; }
        public string? Favicon { get; set; }
        public string? Primary { get; set; }
        public string? Secondary { get; set; }
        public string? LoginBg { get; set; }
        public string? Tagline { get; set; }
        public string? Footer { get; set; }
        public string? Copyright { get; set; }
    }

    private static readonly System.Text.RegularExpressions.Regex HexColor =
        new("^#[0-9A-Fa-f]{6}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    [HttpPost("branding/save"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> SaveBranding(int id, BrandingInput m, CancellationToken ct)
    {
        string? error = null;
        static string? Text(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

        // Panels put these straight into <img src> / CSS, so only plain web addresses.
        string? Url(string? v, string name)
        {
            v = Text(v);
            if (v is null) return null;
            if (v.Length > 500 || !Uri.TryCreate(v, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)
                || v.IndexOfAny(['"', '\'', '<', '>', '(', ')', ' ']) >= 0)
            { error ??= $"{name}: enter a full web address starting with https://"; return null; }
            return v;
        }
        string? Color(string? v, string name)
        {
            v = Text(v);
            if (v is null) return null;
            if (!HexColor.IsMatch(v)) { error ??= $"{name}: use a colour code like #1F5FBF."; return null; }
            return v.ToUpperInvariant();
        }
        string? Limited(string? v, string name, int max)
        {
            v = Text(v);
            if (v is not null && v.Length > max) { error ??= $"{name}: up to {max} characters."; return null; }
            return v;
        }

        var b = new ConfigStore.Branding
        {
            LogoUrl = Url(m.Logo, "Logo"),
            FaviconUrl = Url(m.Favicon, "Browser icon"),
            PrimaryColor = Color(m.Primary, "Main colour"),
            SecondaryColor = Color(m.Secondary, "Second colour"),
            LoginBgUrl = Url(m.LoginBg, "Login background"),
            LoginTagline = Limited(m.Tagline, "Login line", 300),
            FooterText = Limited(m.Footer, "Footer", 500),
            CopyrightText = Limited(m.Copyright, "Copyright", 300),
        };
        if (error is not null)
        {
            // Show the form again with what was typed, so nothing has to be re-entered.
            var company = await _companies.GetAsync(id, ct);
            if (company is null) return NotFound();
            var (_, def) = await _config.BrandingAsync(id, ct);
            ViewData["FlashErrorNow"] = error;
            return View("Index", new CompanyPageModel
            {
                Company = company, Tab = "branding", BrandDefault = def,
                Brand = new ConfigStore.Branding
                {
                    LogoUrl = Text(m.Logo), FaviconUrl = Text(m.Favicon), PrimaryColor = Text(m.Primary),
                    SecondaryColor = Text(m.Secondary), LoginBgUrl = Text(m.LoginBg), LoginTagline = Text(m.Tagline),
                    FooterText = Text(m.Footer), CopyrightText = Text(m.Copyright),
                },
            });
        }

        await _config.SetBrandingAsync(id, b, Actor, ct);
        return Back(id, "branding", b.IsEmpty ? "Branding reset - the panels look as they do today." : "Branding saved.");
    }

    // ---- member dashboard --------------------------------------------------

    [HttpPost("dashboard/save"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> SaveDashboard(int id, string block, string? show, string? width, string? title, CancellationToken ct)
    {
        var b = Catalog.Dashboard.FirstOrDefault(x => x.Key == block);
        if (b is null) return BadRequest();
        title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();

        string? error =
            show is not (null or "" or "show" or "hide") ? "Choose As today, Show or Hide." :
            show == "show" && !b.CanShow ? $"“{b.Today}” can only be hidden - its data is loaded only for some companies." :
            !string.IsNullOrEmpty(width) && (!b.CanResize || Catalog.DashWidths.All(w => w.Value != width)) ? "Choose a width from the list." :
            title is not null && (!b.HasTitle || title.Length > 100) ? "Title: up to 100 characters." :
            null;
        if (error is not null) return Back(id, "dashboard", error, error: true);

        string Key(string part) => $"dash.{b.Key}.{part}";
        if (string.IsNullOrEmpty(show)) await _config.ResetRuleAsync(id, Key("show"), Actor, ct);
        else await _config.SetRuleAsync(id, Key("show"), show, true, Actor, ct);
        if (b.CanResize)
        {
            if (string.IsNullOrEmpty(width)) await _config.ResetRuleAsync(id, Key("width"), Actor, ct);
            else await _config.SetRuleAsync(id, Key("width"), width, true, Actor, ct);
        }
        if (b.HasTitle)
        {
            if (title is null) await _config.ResetLabelAsync(id, Key("title"), Actor, ct);
            else await _config.SetLabelAsync(id, Key("title"), title, Actor, ct);
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(show)) parts.Add(show == "show" ? "Shown" : "Hidden");
        if (!string.IsNullOrEmpty(width)) parts.Add(Catalog.DashWidths.First(w => w.Value == width).Label);
        if (title is not null) parts.Add($"Title: {title}");
        return Back(id, "dashboard", parts.Count == 0 ? $"“{b.Today}” is back to how it is today." : $"Saved: {b.Today} → {string.Join(" · ", parts)}");
    }

    // ---- panel settings (replacing the panels' CompID conditions) ----------

    [HttpPost("panel/save"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> SavePanelSetting(int id, string key, string? value, string? panel, string? page, string? q, CancellationToken ct)
    {
        var s = _catalog.Find(key);
        if (s is null) return BadRequest();
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        IActionResult Done(string msg, bool error = false)
        {
            TempData[error ? "FlashError" : "Flash"] = msg;
            return RedirectToAction(nameof(Index), new { id, tab = "panel", panel = panel ?? s.Panel, page, q });
        }

        switch (s.Kind)
        {
            case "switch":
                if (value is null) { await _config.ResetRuleAsync(id, key, Actor, ct); return Done($"“{s.Label}” is back to how it is today."); }
                if (value is not ("on" or "off")) return Done("Choose As today, On or Off.", true);
                await _config.SetRuleAsync(id, key, value == "on" ? "1" : "0", value == "on", Actor, ct);
                return Done($"Saved: {s.Label} → {(value == "on" ? "On" : "Off")}");
            case "number":
                if (value is null) { await _config.ResetRuleAsync(id, key, Actor, ct); return Done($"“{s.Label}” is back to how it is today."); }
                if (!decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n))
                    return Done("Enter a number.", true);
                await _config.SetRuleAsync(id, key, n.ToString(System.Globalization.CultureInfo.InvariantCulture), true, Actor, ct);
                return Done($"Saved: {s.Label} → {n}");
            case "text":
                if (value is null) { await _config.ResetRuleAsync(id, key, Actor, ct); return Done($"“{s.Label}” is back to how it is today."); }
                if (value.Length > 1000) return Done("Up to 1000 characters.", true);
                await _config.SetRuleAsync(id, key, value, true, Actor, ct);
                return Done($"Saved: {s.Label}");
            default: // label
                if (value is null) { await _config.ResetLabelAsync(id, key, Actor, ct); return Done($"“{s.Label}” is back to how it is today."); }
                if (value.Length > 500) return Done("Up to 500 characters.", true);
                await _config.SetLabelAsync(id, key, value, Actor, ct);
                return Done($"Saved: {s.Label} → {value}");
        }
    }

    // ---- menus (the panels' own menu tables) -------------------------------

    private static readonly System.Text.RegularExpressions.Regex MenuOrder =
        new(@"^\d{1,6}(\.\d{1,6})?$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex MenuLink =
        new(@"^[A-Za-z0-9_\-./?=&#%~:+]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    [HttpPost("menus/save"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> SaveMenu(int id, string panel, long aid, string? name, string? order, string? link, bool shown, CancellationToken ct)
    {
        var p = MenuStore.Find(panel);
        if (p is null || id == 0) return BadRequest();
        name = name?.Trim(); order = order?.Trim(); link = string.IsNullOrWhiteSpace(link) ? null : link.Trim();

        string? error =
            string.IsNullOrEmpty(name) ? "Give the menu a name." :
            name.Length > p.NameMax ? $"Name: up to {p.NameMax} characters." :
            // The member panel turns the order into a number - "2.1.1" would break its menu.
            order is null || order.Length > 20 || !MenuOrder.IsMatch(order) ? "Order: a number like 3 or 3.2." :
            link is not null && (link.Length > p.LinkMax || !MenuLink.IsMatch(link)) ? "Link: a page name like Report.aspx (letters, numbers and . / ? = & # - _ only)." :
            link is not null && link.Contains(':') && !link.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "Link: only a page name or an http(s):// address." :
            null;
        if (error is not null) return BackMenu(id, p, error, error: true);

        var ok = await _menus.UpdateAsync(p, id, aid, name!, order!, link, shown, User.Identity?.Name ?? "?", ct);
        return ok ? BackMenu(id, p, $"Saved: {name} ({(shown ? "shown" : "hidden")})") : BackMenu(id, p, "That menu row was not found for this company.", error: true);
    }

    [HttpPost("menus/add"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> AddMenu(int id, string panel, long menuId, CancellationToken ct)
    {
        var p = MenuStore.Find(panel);
        if (p is null || id == 0) return BadRequest();
        var ok = await _menus.AddFromMasterAsync(p, id, menuId, User.Identity?.Name ?? "?", ct);
        return ok ? BackMenu(id, p, "Menu added and shown.") : BackMenu(id, p, "That menu is already there, or not in the master list.", error: true);
    }

    // ---- websites per panel (Cfg_CompanyHost) --------------------------------

    [HttpPost("websites/add"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> AddWebsite(int id, string? panel, string? host, CancellationToken ct)
    {
        if (id == 0 || await _companies.GetAsync(id, ct) is null) return BadRequest();
        if (!WebsiteStore.Panels.Any(p => p.Key == panel)) return Back(id, "websites", "Choose which panel opens on this address.", error: true);
        var clean = WebsiteStore.Clean(host);
        if (clean is null) return Back(id, "websites", "Type a website name like aventureuser.bisplindia.in (no spaces).", error: true);
        var error = await _websites.AddAsync(id, panel!, clean, User.Identity?.Name ?? "?", ct);
        return error is null ? Back(id, "websites", $"Added: {clean}") : Back(id, "websites", error, error: true);
    }

    // SQL server address every company's connection uses (Proc_GetConnection1); owners only.
    [HttpPost("websites/server"), Authorize(Policy = Policies.Owner)]
    public async Task<IActionResult> SaveServerAddress(int id, string? server, CancellationToken ct)
    {
        if (id != 0) return BadRequest();
        server = server?.Trim();
        if (string.IsNullOrEmpty(server))
        {
            await _config.ResetRuleAsync(0, WebsiteStore.ServerKey, Actor, ct);
            return Back(0, "websites", "Cleared - connection strings are used exactly as stored in M_ConnectionMaster.");
        }
        if (!WebsiteStore.IsServerAddress(server))
            return Back(0, "websites", "Type the server address, then a comma and the port (e.g. SERVER,1433).", error: true);
        await _config.SetRuleAsync(0, WebsiteStore.ServerKey, server, true, Actor, ct);
        return Back(0, "websites", $"Saved: SQL server address → {server}");
    }

    [HttpPost("websites/active"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> WebsiteActive(int id, int hostId, bool active, CancellationToken ct)
    {
        if (id == 0) return BadRequest();
        var ok = await _websites.SetActiveAsync(id, hostId, active, User.Identity?.Name ?? "?", ct);
        return ok ? Back(id, "websites", active ? "Address switched on." : "Address switched off.") : Back(id, "websites", "That address was not found for this company.", error: true);
    }

    [HttpPost("websites/remove"), Authorize(Policy = Policies.CanEdit)]
    public async Task<IActionResult> RemoveWebsite(int id, int hostId, CancellationToken ct)
    {
        if (id == 0) return BadRequest();
        var ok = await _websites.RemoveAsync(id, hostId, User.Identity?.Name ?? "?", ct);
        return ok ? Back(id, "websites", "Address removed.") : Back(id, "websites", "That address was not found for this company.", error: true);
    }

    private IActionResult BackMenu(int id, MenuStore.Panel p, string message, bool error = false)
    {
        TempData[error ? "FlashError" : "Flash"] = message;
        return RedirectToAction(nameof(Index), new { id, tab = "menus", panel = p.Key });
    }

    private IActionResult Back(int id, string tab, string message, bool error = false, string? form = null)
    {
        TempData[error ? "FlashError" : "Flash"] = message;
        return RedirectToAction(nameof(Index), new { id, tab, form });
    }
}
