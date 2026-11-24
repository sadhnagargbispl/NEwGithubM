using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuperAdmin.Data;

/// <summary>
/// The settings that replace the panels' hard-coded company (CompID) conditions.
/// One JSON file per area in SuperAdminConfig/catalog (see CONVERSION-RULES.md);
/// the panels read each key with PanelConfig.IsOn / Text / Number / Label and
/// fall back to today's behaviour when nothing is set. Files are re-read when
/// they change, so a new batch of converted pages shows up without a restart.
/// </summary>
public sealed class SettingsCatalog
{
    public sealed class Setting
    {
        public string Key { get; init; } = "";
        public string Panel { get; init; } = "";
        public string Page { get; init; } = "";
        public string Kind { get; init; } = "switch";   // switch | text | number | label
        public string Label { get; init; } = "";
        public string? Today { get; init; }
        public string? Where { get; init; }

        /// <summary>
        /// True for switches that choose WHICH query / procedure / save statement /
        /// data layout runs. Turning one on for a company whose database is shaped
        /// differently can break that page - Super Admin warns and asks first.
        /// </summary>
        public bool Advanced => AdvancedKey.IsMatch(Key)
            || Label.StartsWith("Advanced", StringComparison.OrdinalIgnoreCase)
            || (Label + " " + Today).Contains("break", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly System.Text.RegularExpressions.Regex AdvancedKey = new(
        @"(auto_verif|layout|query|_sp_|use_sp|sp_view|net_pay|tolerate|matched_bv|\.save_|username_as_id|lookup_|kit_list|balance_|activate_|create_purchase|product_list|r_filter|r_group|r_sum|unit_ev|tree_js|rp_tree|three_legs|depth_\d|reset_kyc|company_kyc|edit_rules|show_level_income_matching|show_downline_income_upline)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private sealed class FileShape
    {
        public List<Setting> Settings { get; init; } = [];
    }

    public static readonly (string Key, string Name)[] PanelNames =
        [("member", "Member cPanel"), ("admin", "Admin panel"), ("franchise", "Franchise panel")];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly string _folder;
    private readonly ILogger<SettingsCatalog> _log;
    private readonly object _gate = new();
    private string _stamp = "";
    private IReadOnlyList<Setting> _all = [];
    private IReadOnlyDictionary<string, Setting> _byKey = new Dictionary<string, Setting>();

    public SettingsCatalog(IConfiguration config, IWebHostEnvironment env, ILogger<SettingsCatalog> log)
    {
        _folder = Path.GetFullPath(config["Catalog:Folder"] ?? Path.Combine(env.ContentRootPath, "..", "SuperAdminConfig", "catalog"));
        _log = log;
    }

    public IReadOnlyList<Setting> All { get { Refresh(); return _all; } }

    public Setting? Find(string key) { Refresh(); return _byKey.TryGetValue(key, out var s) ? s : null; }

    private void Refresh()
    {
        if (!Directory.Exists(_folder)) return;
        var files = Directory.GetFiles(_folder, "*.json").OrderBy(f => f).ToArray();
        var stamp = string.Join("|", files.Select(f => f + File.GetLastWriteTimeUtc(f).Ticks));
        if (stamp == _stamp) return;

        lock (_gate)
        {
            if (stamp == _stamp) return;
            var all = new List<Setting>();
            foreach (var f in files)
            {
                try
                {
                    var shape = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(f), Json);
                    all.AddRange((shape?.Settings ?? []).Where(Valid));
                }
                catch (Exception ex)
                {
                    // A half-written file (an agent still working) must not break the page.
                    _log.LogWarning(ex, "Catalog file {File} could not be read; skipped for now", f);
                }
            }
            // First file wins if two list the same key.
            var byKey = new Dictionary<string, Setting>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in all) byKey.TryAdd(s.Key, s);
            _all = byKey.Values.OrderBy(s => s.Panel).ThenBy(s => s.Page, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Label).ToList();
            _byKey = byKey;
            _stamp = stamp;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex KeyShape = new(@"^[a-z0-9_.]{3,100}$");

    private static bool Valid(Setting s) =>
        KeyShape.IsMatch(s.Key) && s.Kind is "switch" or "text" or "number" or "label" && !string.IsNullOrWhiteSpace(s.Label);
}
