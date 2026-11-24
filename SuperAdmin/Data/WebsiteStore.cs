using System.Text.RegularExpressions;
using Dapper;

namespace SuperAdmin.Data;

/// <summary>
/// Website addresses per panel (master table Cfg_CompanyHost, SuperAdminConfig\06_company_hosts.sql).
/// The panels find the company by the website they open on. When member and admin
/// panels use unrelated names (aventureuser.x.in / aventureadmin.x.in) each name is
/// listed here; the panels' PanelConfig.CompanyUrl / PanelUrl read it.
/// A company with no rows works exactly as before.
/// </summary>
public sealed partial class WebsiteStore
{
    private readonly Db _db;
    public WebsiteStore(Db db) => _db = db;

    public static readonly (string Key, string Name)[] Panels =
        [("member", "Member panel (cPanel)"), ("admin", "Admin panel"), ("website", "Main website")];

    public sealed record Site(int HostID, int CompanyID, string Panel, string Host, bool IsActive, DateTime AddDate, string? AddedBy);

    public sealed class Page
    {
        public bool Ready { get; set; }           // table exists
        public string? CompanyUrl { get; set; }   // M_CompanyMasterNew.URL
        public List<Site> Sites { get; } = [];
    }

    // a bare host name: letters, digits, dots and dashes; at least one dot
    [GeneratedRegex(@"^(?=.{3,200}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9-]{2,63}$")] private static partial Regex HostRx();

    /// <summary>Turns what was typed (maybe with http://, path, port, www.) into a bare host, or null.</summary>
    public static string? Clean(string? typed)
    {
        var s = (typed ?? "").Trim().ToLowerInvariant();
        if (s.StartsWith("http://")) s = s[7..];
        else if (s.StartsWith("https://")) s = s[8..];
        var cut = s.IndexOfAny(['/', '?', '#', ':']);
        if (cut >= 0) s = s[..cut];
        s = s.TrimEnd('.');
        return HostRx().IsMatch(s) ? s : null;
    }

    public async Task<Page> GetAsync(int companyId, CancellationToken ct = default)
    {
        var p = new Page();
        await using var c = await _db.OpenAsync(ct);
        p.CompanyUrl = await c.ExecuteScalarAsync<string?>("SELECT URL FROM dbo.M_CompanyMasterNew WHERE Id = @companyId;", new { companyId });
        p.Ready = await c.ExecuteScalarAsync<int?>("SELECT OBJECT_ID('dbo.Cfg_CompanyHost', 'U');") is not null;
        if (!p.Ready) return p;
        p.Sites.AddRange(await c.QueryAsync<Site>(
            "SELECT HostID, CompanyID, Panel, Host, IsActive, AddDate, AddedBy FROM dbo.Cfg_CompanyHost WHERE CompanyID = @companyId ORDER BY Panel, HostID;",
            new { companyId }));
        return p;
    }

    // ---- SQL server address (Cfg_Feature, All companies, key db.server_address) ----
    // Proc_GetConnection1 swaps "localhost,1533" in stored connection strings for it
    // (SuperAdminConfig\07_server_address_setting.sql). Empty = strings used as stored.
    public const string ServerKey = "db.server_address";

    [GeneratedRegex(@"^[A-Za-z0-9.\-]{1,200}(\\[A-Za-z0-9_$\-]{1,50})?(,\d{1,5})?$")] private static partial Regex ServerRx();
    public static bool IsServerAddress(string? v) => v is not null && ServerRx().IsMatch(v);

    public async Task<(string? Value, bool On)> ServerAddressAsync(CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        var row = await c.QueryFirstOrDefaultAsync<(string? Value, bool On)?>(
            "SELECT FeatureValue, IsEnabled FROM dbo.Cfg_Feature WHERE CompanyID = 0 AND FeatureKey = @k;", new { k = ServerKey });
        return row ?? (null, false);
    }

    /// <summary>Adds an address. Returns an error message, or null when saved.</summary>
    public async Task<string?> AddAsync(int companyId, string panel, string host, string by, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        var owner = await c.QueryFirstOrDefaultAsync<(int CompanyID, string? Name)?>(
            "SELECT h.CompanyID, m.Name FROM dbo.Cfg_CompanyHost h WITH (UPDLOCK, HOLDLOCK) LEFT JOIN dbo.M_CompanyMasterNew m ON m.Id = h.CompanyID WHERE h.Host = @host;",
            new { host }, tx);
        if (owner is not null)
            return owner.Value.CompanyID == companyId ? $"{host} is already listed for this company."
                                                       : $"{host} already belongs to company {owner.Value.CompanyID} {owner.Value.Name}.";
        // An address that is another company's own URL would send that company's visitors here.
        var other = await c.QueryFirstOrDefaultAsync<(int Id, string? Name)?>(
            "SELECT Id, Name FROM dbo.M_CompanyMasterNew WHERE Id <> @companyId AND UPPER(LTRIM(RTRIM(URL))) = UPPER(@host);",
            new { companyId, host }, tx);
        if (other is not null) return $"{host} is the website of company {other.Value.Id} {other.Value.Name}.";

        await c.ExecuteAsync("INSERT INTO dbo.Cfg_CompanyHost (CompanyID, Panel, Host, AddedBy) VALUES (@companyId, @panel, @host, @by);",
            new { companyId, panel, host, by }, tx);
        await ConfigStore.LogAsync(c, tx, companyId, "website", $"{panel}:{host}", "add", null, host, by);
        await tx.CommitAsync(ct);
        return null;
    }

    public async Task<bool> SetActiveAsync(int companyId, int hostId, bool active, string by, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        var row = await c.QueryFirstOrDefaultAsync<Site>(
            "SELECT HostID, CompanyID, Panel, Host, IsActive, AddDate, AddedBy FROM dbo.Cfg_CompanyHost WHERE HostID = @hostId AND CompanyID = @companyId;",
            new { hostId, companyId }, tx);
        if (row is null) return false;
        if (row.IsActive != active)
        {
            await c.ExecuteAsync("UPDATE dbo.Cfg_CompanyHost SET IsActive = @active WHERE HostID = @hostId AND CompanyID = @companyId;",
                new { active, hostId, companyId }, tx);
            await ConfigStore.LogAsync(c, tx, companyId, "website", $"{row.Panel}:{row.Host}", active ? "on" : "off",
                row.IsActive ? "on" : "off", active ? "on" : "off", by);
        }
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> RemoveAsync(int companyId, int hostId, string by, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        var row = await c.QueryFirstOrDefaultAsync<Site>(
            "SELECT HostID, CompanyID, Panel, Host, IsActive, AddDate, AddedBy FROM dbo.Cfg_CompanyHost WHERE HostID = @hostId AND CompanyID = @companyId;",
            new { hostId, companyId }, tx);
        if (row is null) return false;
        await c.ExecuteAsync("DELETE FROM dbo.Cfg_CompanyHost WHERE HostID = @hostId AND CompanyID = @companyId;", new { hostId, companyId }, tx);
        await ConfigStore.LogAsync(c, tx, companyId, "website", $"{row.Panel}:{row.Host}", "delete", row.Host, null, by);
        await tx.CommitAsync(ct);
        return true;
    }
}
