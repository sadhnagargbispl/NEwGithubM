using Dapper;

namespace SuperAdmin.Data;

/// <summary>The companies in the legacy registry (M_CompanyMasterNew) + how much is set for each.</summary>
public sealed class CompanyStore
{
    private readonly Db _db;
    public CompanyStore(Db db) => _db = db;

    public sealed class Company
    {
        public int Id { get; init; }
        public string? Name { get; init; }
        public string? Url { get; init; }
        public string? Logo { get; init; }
        public bool IsActive { get; init; }
        public int Version { get; init; }        // Cfg_Version (0 = nothing set yet)
        public int SettingsCount { get; init; }  // rows the company has in Cfg_Label/ValueType/Feature/FormField
    }

    public async Task<IReadOnlyList<Company>> ListAsync(CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        return (await c.QueryAsync<Company>("""
            SELECT m.ID AS Id, m.Name, m.URL AS Url, m.Logo,
                   CAST(CASE WHEN m.IsActive = 1 THEN 1 ELSE 0 END AS bit) AS IsActive,
                   ISNULL(v.Version, 0) AS Version,
                   (SELECT COUNT(*) FROM dbo.Cfg_Label     WHERE CompanyID = m.ID)
                 + (SELECT COUNT(*) FROM dbo.Cfg_ValueType WHERE CompanyID = m.ID)
                 + (SELECT COUNT(*) FROM dbo.Cfg_Feature   WHERE CompanyID = m.ID)
                 + (SELECT COUNT(*) FROM dbo.Cfg_FormField WHERE CompanyID = m.ID) AS SettingsCount
              FROM dbo.M_CompanyMasterNew m
              LEFT JOIN dbo.Cfg_Version v ON v.CompanyID = m.ID
             ORDER BY CASE WHEN m.IsActive = 1 THEN 0 ELSE 1 END, m.Name;
            """)).AsList();
    }

    public async Task<Company?> GetAsync(int id, CancellationToken ct = default)
    {
        if (id == 0) return new Company { Id = 0, Name = "All companies", IsActive = true };
        await using var c = await _db.OpenAsync(ct);
        return await c.QueryFirstOrDefaultAsync<Company>("""
            SELECT m.ID AS Id, m.Name, m.URL AS Url, m.Logo,
                   CAST(CASE WHEN m.IsActive = 1 THEN 1 ELSE 0 END AS bit) AS IsActive,
                   ISNULL((SELECT Version FROM dbo.Cfg_Version WHERE CompanyID = m.ID), 0) AS Version
              FROM dbo.M_CompanyMasterNew m WHERE m.ID = @id;
            """, new { id });
    }
}
