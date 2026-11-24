using SuperAdmin.Data;

namespace SuperAdmin.Models;

public sealed class CompanyPageModel
{
    public required CompanyStore.Company Company { get; init; }
    public required string Tab { get; init; }
    public ConfigStore.Layer Layer { get; set; } = new();
    public IReadOnlyList<ConfigStore.Change> Changes { get; set; } = [];

    // Forms tab
    public Catalog.Form? Form { get; set; }
    public ConfigStore.FieldLayer Fields { get; set; } = new();

    // Branding tab: own values + the All-companies values they fall back to
    public ConfigStore.Branding? Brand { get; set; }
    public ConfigStore.Branding? BrandDefault { get; set; }

    // Dashboard tab: titles (show / width come in Layer)
    public ConfigStore.Layer DashLabels { get; set; } = new();

    // Panel settings tab (catalog of converted CompID conditions)
    public string SettingsPanel { get; set; } = "member";
    public string? SettingsPage { get; set; }
    public string? SettingsQuery { get; set; }
    public IReadOnlyList<(string Page, int Count)> SettingsPages { get; set; } = [];
    public IReadOnlyDictionary<string, int> SettingsCountByPanel { get; set; } = new Dictionary<string, int>();
    public IReadOnlyList<SettingsCatalog.Setting> Settings { get; set; } = [];

    // Company details tab (M_CompanyMaster in the company's database);
    // DetailsTyped = what was typed when a save was refused, shown back in the boxes
    public CompanyDetailsStore.Details? Details { get; set; }
    public IReadOnlyDictionary<string, string>? DetailsTyped { get; set; }

    // Websites tab (Cfg_CompanyHost)
    public WebsiteStore.Page? Websites { get; set; }
    public (string? Value, bool On) ServerAddress { get; set; }   // All companies only

    // Menus tab
    public MenuStore.Panel? MenuPanel { get; set; }
    public IReadOnlyList<MenuStore.MenuRow> Menu { get; set; } = [];
    public IReadOnlyList<MenuStore.MasterRow> MenuMissing { get; set; } = [];
}
