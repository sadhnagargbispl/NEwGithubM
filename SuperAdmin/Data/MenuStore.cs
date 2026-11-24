using Dapper;
using Microsoft.Data.SqlClient;

namespace SuperAdmin.Data;

/// <summary>
/// The panels' own company menus - the tables the panels read on every page:
///   Admin panel   : M_CompWiseWebMenuMaster    (master list M_WebMenuMaster)
///   Member cPanel : M_CompWiseWebMenuMasterDis (master list M_WebMenuMasterDis)
/// A row shows in a panel only when ActiveStatus = 'Y' AND RowStatus = 'Y'
/// (on/off sets both, like the old Enable_disable page). Rows are identified by
/// AId: the same MenuId can appear twice for one company in today's data.
/// Changes take effect in the panel on its next page load; every change is
/// also written to Cfg_ChangeLog (area "menu").
/// </summary>
public sealed class MenuStore
{
    private readonly Db _db;
    public MenuStore(Db db) => _db = db;

    public sealed record Panel(string Key, string Name, string CompanyTable, string MasterTable, int NameMax, int LinkMax);

    public static readonly Panel[] Panels =
    [
        new("admin", "Admin panel menu", "M_CompWiseWebMenuMaster", "M_WebMenuMaster", 100, 500),
        new("member", "Member (distributor) menu", "M_CompWiseWebMenuMasterDis", "M_WebMenuMasterDis", 500, 500),
    ];

    public static Panel? Find(string? key) => Panels.FirstOrDefault(p => p.Key == key);

    public sealed class MenuRow
    {
        public long AId { get; init; }
        public long MenuId { get; init; }
        public long ParentId { get; init; }
        public string Hierar { get; init; } = "";
        public string? MenuName { get; init; }
        public string? OnSelect { get; init; }
        public string ActiveStatus { get; init; } = "";
        public string RowStatus { get; init; } = "";
        public string? LastModified { get; init; }
        public bool InMaster { get; init; }

        public bool Shown => ActiveStatus.Trim() == "Y" && RowStatus.Trim() == "Y";
        /// <summary>"-" is how the panels mark an item they skip.</summary>
        public bool IsDash => (MenuName ?? "").Trim() == "-";
    }

    public sealed class MasterRow
    {
        public long MenuId { get; init; }
        public long ParentId { get; init; }
        public string Hierar { get; init; } = "";
        public string? MenuName { get; init; }
        public string? OnSelect { get; init; }
    }

    // Table names come only from the fixed Panels list above, never from input.
    public async Task<IReadOnlyList<MenuRow>> CompanyMenuAsync(Panel p, int companyId, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        return (await c.QueryAsync<MenuRow>($"""
            SELECT CAST(a.AId AS bigint) AS AId, CAST(a.MenuId AS bigint) AS MenuId, CAST(a.ParentId AS bigint) AS ParentId,
                   RTRIM(LTRIM(a.Hierar)) AS Hierar, a.MenuName, CAST(a.OnSelect AS nvarchar(500)) AS OnSelect,
                   a.ActiveStatus, a.RowStatus, a.LastModified,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.{p.MasterTable} m WHERE m.MenuId = a.MenuId) THEN 1 ELSE 0 END AS bit) AS InMaster
              FROM dbo.{p.CompanyTable} a
             WHERE a.CompanyID = @companyId
             ORDER BY CASE WHEN ISNUMERIC(a.Hierar) = 1 THEN CONVERT(decimal(18, 6), RTRIM(LTRIM(a.Hierar))) ELSE 0 END, a.MenuId, a.AId;
            """, new { companyId })).AsList();
    }

    /// <summary>Master-list menus this company does not have yet.</summary>
    public async Task<IReadOnlyList<MasterRow>> MissingAsync(Panel p, int companyId, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        return (await c.QueryAsync<MasterRow>($"""
            SELECT CAST(m.MenuId AS bigint) AS MenuId, CAST(m.ParentId AS bigint) AS ParentId,
                   RTRIM(LTRIM(m.Hierar)) AS Hierar, m.MenuName, m.OnSelect
              FROM dbo.{p.MasterTable} m
             WHERE NOT EXISTS (SELECT 1 FROM dbo.{p.CompanyTable} a WHERE a.CompanyID = @companyId AND a.MenuId = m.MenuId)
             ORDER BY CASE WHEN ISNUMERIC(m.Hierar) = 1 THEN CONVERT(decimal(18, 6), RTRIM(LTRIM(m.Hierar))) ELSE 0 END, m.MenuId;
            """, new { companyId })).AsList();
    }

    // ---- friendly page names ------------------------------------------------
    // The Panel settings list is grouped by page FILE (e.g. ApprovePaymentReqs.aspx).
    // The master menu lists say what each page is called in the panel's menu, so
    // that name is shown instead. Read once per 10 minutes; any error = no names.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, IReadOnlyDictionary<string, string> Names)> PageNameCache = new();
    private static readonly System.Text.RegularExpressions.Regex AspxFile =
        new(@"([A-Za-z0-9_\-\. ]+\.aspx)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Page file (lower case, e.g. "approvepaymentreqs.aspx") → its menu name, for "admin" / "member".</summary>
    public async Task<IReadOnlyDictionary<string, string>> PageNamesAsync(string panelKey, CancellationToken ct = default)
    {
        var p = Find(panelKey);
        if (p is null) return new Dictionary<string, string>();
        if (PageNameCache.TryGetValue(p.Key, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(10)) return hit.Names;
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var c = await _db.OpenAsync(ct);
            // Master list first; then the companies' own menus (pages some companies added themselves).
            var rows = await c.QueryAsync<(string? MenuName, string? OnSelect)>($"""
                SELECT MenuName, OnSelect FROM (
                    SELECT 0 AS Src, CAST(MenuId AS bigint) AS MenuId, MenuName, CAST(OnSelect AS nvarchar(500)) AS OnSelect FROM dbo.{p.MasterTable} WHERE OnSelect IS NOT NULL
                    UNION ALL
                    SELECT 1, CAST(MenuId AS bigint), MenuName, CAST(OnSelect AS nvarchar(500)) FROM dbo.{p.CompanyTable} WHERE OnSelect IS NOT NULL
                ) x ORDER BY Src, MenuId;
                """);
            foreach (var (name, link) in rows)
            {
                var n = (name ?? "").Trim();
                if (n.Length == 0 || n == "-") continue;
                var m = AspxFile.Match(link ?? "");
                if (!m.Success) continue;
                var file = System.IO.Path.GetFileName(m.Groups[1].Value.Trim());
                names.TryAdd(file, n);   // first (lowest MenuId) wins
            }
        }
        catch (Exception) { /* names are only a help - the file name is still shown */ }
        PageNameCache[p.Key] = (DateTime.UtcNow, names);
        return names;
    }

    public static string Describe(string? name, string hierar, string? link, bool shown) =>
        $"{(string.IsNullOrWhiteSpace(name) ? "(no name)" : name.Trim())} · order {hierar.Trim()} · {(string.IsNullOrWhiteSpace(link) ? "no link" : link.Trim())} · {(shown ? "shown" : "hidden")}";

    /// <summary>Updates one row of this company. False if the row is not this company's.</summary>
    public async Task<bool> UpdateAsync(Panel p, int companyId, long aid, string name, string hierar, string? link,
                                        bool shown, string by, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        var old = await c.QueryFirstOrDefaultAsync<MenuRow>($"""
            SELECT CAST(AId AS bigint) AS AId, CAST(MenuId AS bigint) AS MenuId, RTRIM(LTRIM(Hierar)) AS Hierar, MenuName,
                   CAST(OnSelect AS nvarchar(500)) AS OnSelect, ActiveStatus, RowStatus
              FROM dbo.{p.CompanyTable} WITH (UPDLOCK) WHERE AId = @aid AND CompanyID = @companyId;
            """, new { aid, companyId }, tx);
        if (old is null) { await tx.CommitAsync(ct); return false; }

        var before = Describe(old.MenuName, old.Hierar, old.OnSelect, old.Shown);
        var after = Describe(name, hierar, link, shown);
        if (before == after) { await tx.CommitAsync(ct); return true; }

        var flag = shown ? "Y" : "N";
        await c.ExecuteAsync($"""
            UPDATE dbo.{p.CompanyTable}
               SET MenuName = @name, Hierar = @hierar, OnSelect = @link, ActiveStatus = @flag, RowStatus = @flag,
                   LastModified = @lastModified
             WHERE AId = @aid AND CompanyID = @companyId;
            """, new { name, hierar, link = link ?? "", flag, lastModified = Stamp(by), aid, companyId }, tx);
        await ConfigStore.LogAsync(c, tx, companyId, "menu", $"{p.Key}:{old.MenuId}", "update", before, after, by);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>Copies a master-list menu into this company (shown). False if already there / unknown.</summary>
    public async Task<bool> AddFromMasterAsync(Panel p, int companyId, long menuId, string by, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        var m = await c.QueryFirstOrDefaultAsync<MasterRow>($"""
            SELECT TOP 1 CAST(MenuId AS bigint) AS MenuId, CAST(ParentId AS bigint) AS ParentId, RTRIM(LTRIM(Hierar)) AS Hierar, MenuName, OnSelect
              FROM dbo.{p.MasterTable} WHERE MenuId = @menuId;
            """, new { menuId }, tx);
        var exists = await c.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.{p.CompanyTable} WITH (UPDLOCK, HOLDLOCK) WHERE CompanyID = @companyId AND MenuId = @menuId;",
            new { companyId, menuId }, tx);
        if (m is null || exists > 0) { await tx.CommitAsync(ct); return false; }

        // Same columns the master row has; IP / MAC are not known here.
        await c.ExecuteAsync($"""
            INSERT INTO dbo.{p.CompanyTable} (MenuId, Hierar, MenuName, ParentId, OnSelect, ShortKeys, RecTimeStamp, LastModified,
                                              ActiveStatus, RowStatus, IPAdrs, MACAdrs, CompanyID)
            SELECT TOP 1 MenuId, Hierar, MenuName, ParentId, OnSelect, ShortKeys, GETDATE(), @lastModified,
                   'Y', 'Y', '', '', @companyId
              FROM dbo.{p.MasterTable} WHERE MenuId = @menuId;
            """, new { menuId, companyId, lastModified = Stamp(by) }, tx);
        await ConfigStore.LogAsync(c, tx, companyId, "menu", $"{p.Key}:{menuId}", "insert", null,
                                   Describe(m.MenuName, m.Hierar, m.OnSelect, true), by);
        await tx.CommitAsync(ct);
        return true;
    }

    private static string Stamp(string by) => $"Super Admin: {by} {DateTime.Now:dd-MMM-yyyy HH:mm}";
}
