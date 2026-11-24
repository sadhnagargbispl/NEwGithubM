using Dapper;
using Microsoft.Data.SqlClient;

namespace SuperAdmin.Data;

/// <summary>
/// Per-company settings the panels read (Cfg_* tables, script 01). CompanyID 0
/// = the default for ALL companies; a company's own row wins. Every change is
/// written to Cfg_ChangeLog and raises the company's Cfg_Version, so the panels
/// pick it up within their re-check interval. Nothing set = panel as today.
/// </summary>
public sealed class ConfigStore
{
    private readonly Db _db;
    public ConfigStore(Db db) => _db = db;

    public sealed record Entry(int CompanyID, string Key, string? Value, bool IsEnabled, int SortOrder, string? UpdatedBy, DateTime UpdatedAt);

    /// <summary>The company's rows and the "All companies" (0) rows for one area.</summary>
    public sealed class Layer
    {
        public Dictionary<string, Entry> Own { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Entry> Inherited { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Layer> LabelsAsync(int companyId, CancellationToken ct = default) => await LoadAsync(companyId,
        "SELECT CompanyID, LabelKey AS [Key], LabelText AS Value, CAST(1 AS bit) AS IsEnabled, 0 AS SortOrder, UpdatedBy, UpdatedAt FROM dbo.Cfg_Label WHERE CompanyID IN (@companyId, 0);", ct);

    public async Task<Layer> UnitsAsync(int companyId, CancellationToken ct = default) => await LoadAsync(companyId,
        "SELECT CompanyID, Code AS [Key], DisplayName AS Value, IsEnabled, SortOrder, UpdatedBy, UpdatedAt FROM dbo.Cfg_ValueType WHERE CompanyID IN (@companyId, 0);", ct);

    public async Task<Layer> RulesAsync(int companyId, CancellationToken ct = default) => await LoadAsync(companyId,
        "SELECT CompanyID, FeatureKey AS [Key], FeatureValue AS Value, IsEnabled, 0 AS SortOrder, UpdatedBy, UpdatedAt FROM dbo.Cfg_Feature WHERE CompanyID IN (@companyId, 0);", ct);

    private async Task<Layer> LoadAsync(int companyId, string sql, CancellationToken ct)
    {
        await using var c = await _db.OpenAsync(ct);
        var layer = new Layer();
        foreach (var e in await c.QueryAsync<Entry>(sql, new { companyId }))
            (e.CompanyID == companyId ? layer.Own : layer.Inherited)[e.Key] = e;
        if (companyId == 0) layer.Inherited.Clear();
        return layer;
    }

    public sealed record Change(long LogID, int CompanyID, string Area, string ItemKey, string Action,
                                string? OldValue, string? NewValue, string? ChangedBy, DateTime ChangedAt);

    public async Task<IReadOnlyList<Change>> ChangesAsync(int companyId, int take = 200, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        return (await c.QueryAsync<Change>("""
            SELECT TOP (@take) LogID, CompanyID, Area, ItemKey, Action, OldValue, NewValue, ChangedBy, ChangedAt
              FROM dbo.Cfg_ChangeLog
             WHERE CompanyID IN (@companyId, 0)
             ORDER BY ChangedAt DESC, LogID DESC;
            """, new { companyId, take })).AsList();
    }

    // ---- write -------------------------------------------------------------

    public Task SetLabelAsync(int companyId, string key, string text, string by, CancellationToken ct = default) =>
        UpsertAsync(companyId, "label", key, by, ct,
            select: "SELECT LabelText FROM dbo.Cfg_Label WHERE CompanyID = @companyId AND LabelKey = @key;",
            upsert: """
                IF EXISTS (SELECT 1 FROM dbo.Cfg_Label WHERE CompanyID = @companyId AND LabelKey = @key)
                    UPDATE dbo.Cfg_Label SET LabelText = @text, UpdatedBy = @by, UpdatedAt = GETDATE()
                     WHERE CompanyID = @companyId AND LabelKey = @key;
                ELSE
                    INSERT INTO dbo.Cfg_Label (CompanyID, LabelKey, LabelText, UpdatedBy) VALUES (@companyId, @key, @text, @by);
                """,
            args: new { companyId, key, text, by }, newValue: text);

    public Task ResetLabelAsync(int companyId, string key, string by, CancellationToken ct = default) =>
        DeleteAsync(companyId, "label", key, by, ct,
            select: "SELECT LabelText FROM dbo.Cfg_Label WHERE CompanyID = @companyId AND LabelKey = @key;",
            delete: "DELETE FROM dbo.Cfg_Label WHERE CompanyID = @companyId AND LabelKey = @key;");

    public Task SetUnitAsync(int companyId, string code, string name, bool enabled, int sort, string by, CancellationToken ct = default) =>
        UpsertAsync(companyId, "valuetype", code, by, ct,
            select: "SELECT DisplayName + '|' + CAST(IsEnabled AS varchar) + '|' + CAST(SortOrder AS varchar) FROM dbo.Cfg_ValueType WHERE CompanyID = @companyId AND Code = @key;",
            upsert: """
                IF EXISTS (SELECT 1 FROM dbo.Cfg_ValueType WHERE CompanyID = @companyId AND Code = @key)
                    UPDATE dbo.Cfg_ValueType SET DisplayName = @name, IsEnabled = @enabled, SortOrder = @sort,
                           UpdatedBy = @by, UpdatedAt = GETDATE()
                     WHERE CompanyID = @companyId AND Code = @key;
                ELSE
                    INSERT INTO dbo.Cfg_ValueType (CompanyID, Code, DisplayName, IsEnabled, SortOrder, UpdatedBy)
                    VALUES (@companyId, @key, @name, @enabled, @sort, @by);
                """,
            args: new { companyId, key = code, name, enabled, sort, by }, newValue: $"{name}|{(enabled ? 1 : 0)}|{sort}");

    public Task ResetUnitAsync(int companyId, string code, string by, CancellationToken ct = default) =>
        DeleteAsync(companyId, "valuetype", code, by, ct,
            select: "SELECT DisplayName FROM dbo.Cfg_ValueType WHERE CompanyID = @companyId AND Code = @key;",
            delete: "DELETE FROM dbo.Cfg_ValueType WHERE CompanyID = @companyId AND Code = @key;");

    public Task SetRuleAsync(int companyId, string key, string? value, bool enabled, string by, CancellationToken ct = default) =>
        UpsertAsync(companyId, "feature", key, by, ct,
            select: "SELECT ISNULL(FeatureValue, '') + '|' + CAST(IsEnabled AS varchar) FROM dbo.Cfg_Feature WHERE CompanyID = @companyId AND FeatureKey = @key;",
            upsert: """
                IF EXISTS (SELECT 1 FROM dbo.Cfg_Feature WHERE CompanyID = @companyId AND FeatureKey = @key)
                    UPDATE dbo.Cfg_Feature SET FeatureValue = @value, IsEnabled = @enabled, UpdatedBy = @by, UpdatedAt = GETDATE()
                     WHERE CompanyID = @companyId AND FeatureKey = @key;
                ELSE
                    INSERT INTO dbo.Cfg_Feature (CompanyID, FeatureKey, FeatureValue, IsEnabled, UpdatedBy)
                    VALUES (@companyId, @key, @value, @enabled, @by);
                """,
            args: new { companyId, key, value, enabled, by }, newValue: $"{value}|{(enabled ? 1 : 0)}");

    public Task ResetRuleAsync(int companyId, string key, string by, CancellationToken ct = default) =>
        DeleteAsync(companyId, "feature", key, by, ct,
            select: "SELECT ISNULL(FeatureValue, '') + '|' + CAST(IsEnabled AS varchar) FROM dbo.Cfg_Feature WHERE CompanyID = @companyId AND FeatureKey = @key;",
            delete: "DELETE FROM dbo.Cfg_Feature WHERE CompanyID = @companyId AND FeatureKey = @key;");

    // ---- form fields ---------------------------------------------------------

    /// <summary>One field's rule. Null = "as today" (the panel decides).</summary>
    public sealed class FieldRule
    {
        public int CompanyID { get; init; }
        public string FieldKey { get; init; } = "";
        public bool? IsVisible { get; init; }
        public bool? IsRequired { get; init; }
        public bool? IsReadOnly { get; init; }
        public int? MinLength { get; init; }
        public int? MaxLength { get; init; }
        public string? Pattern { get; init; }
        public string? LabelText { get; init; }
        public string? Placeholder { get; init; }
        public string? HelpText { get; init; }
        public int? SortOrder { get; init; }
        public string? UpdatedBy { get; init; }
        public DateTime UpdatedAt { get; init; }

        /// <summary>True when nothing is chosen (saving it = same as reset).</summary>
        public bool IsEmpty => IsVisible is null && IsRequired is null && IsReadOnly is null && MinLength is null
            && MaxLength is null && Pattern is null && LabelText is null && Placeholder is null && HelpText is null && SortOrder is null;

        /// <summary>What is set, in plain words - also what the change log shows.</summary>
        public string Describe()
        {
            var p = new List<string>();
            if (IsVisible is { } v) p.Add(v ? "Shown" : "Hidden");
            if (IsRequired is { } r) p.Add(r ? "Required" : "Optional");
            if (IsReadOnly is { } ro) p.Add(ro ? "Read-only" : "Editable");
            if (LabelText is not null) p.Add($"Label: {LabelText}");
            if (Placeholder is not null) p.Add($"Hint: {Placeholder}");
            if (HelpText is not null) p.Add($"Help: {HelpText}");
            if (MinLength is not null) p.Add($"Min {MinLength}");
            if (MaxLength is not null) p.Add($"Max {MaxLength}");
            if (Pattern is not null) p.Add($"Pattern: {Pattern}");
            if (SortOrder is not null) p.Add($"Order {SortOrder}");
            return p.Count == 0 ? "as today" : string.Join(" · ", p);
        }
    }

    public sealed class FieldLayer
    {
        public Dictionary<string, FieldRule> Own { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, FieldRule> Inherited { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<FieldLayer> FormFieldsAsync(int companyId, string formKey, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        var layer = new FieldLayer();
        foreach (var f in await c.QueryAsync<FieldRule>("""
            SELECT CompanyID, FieldKey, IsVisible, IsRequired, IsReadOnly, MinLength, MaxLength, Pattern,
                   LabelText, Placeholder, HelpText, SortOrder, UpdatedBy, UpdatedAt
              FROM dbo.Cfg_FormField WHERE CompanyID IN (@companyId, 0) AND FormKey = @formKey;
            """, new { companyId, formKey }))
            (f.CompanyID == companyId ? layer.Own : layer.Inherited)[f.FieldKey] = f;
        if (companyId == 0) layer.Inherited.Clear();
        return layer;
    }

    /// <summary>Saves a field's rule (an all-empty rule removes the row: back to today).</summary>
    public async Task SetFormFieldAsync(int companyId, string formKey, FieldRule rule, string by, CancellationToken ct = default)
    {
        if (rule.IsEmpty) { await ResetFormFieldAsync(companyId, formKey, rule.FieldKey, by, ct); return; }

        var p = new { companyId, formKey, fieldKey = rule.FieldKey };
        await using var c = await _db.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        var old = await c.QueryFirstOrDefaultAsync<FieldRule>(
            "SELECT * FROM dbo.Cfg_FormField WHERE CompanyID = @companyId AND FormKey = @formKey AND FieldKey = @fieldKey;", p, tx);
        var newText = rule.Describe();
        if (old is not null && old.Describe() == newText) { await tx.CommitAsync(ct); return; }

        await c.ExecuteAsync("""
            IF EXISTS (SELECT 1 FROM dbo.Cfg_FormField WHERE CompanyID = @companyId AND FormKey = @formKey AND FieldKey = @fieldKey)
                UPDATE dbo.Cfg_FormField
                   SET IsVisible = @IsVisible, IsRequired = @IsRequired, IsReadOnly = @IsReadOnly,
                       MinLength = @MinLength, MaxLength = @MaxLength, Pattern = @Pattern,
                       LabelText = @LabelText, Placeholder = @Placeholder, HelpText = @HelpText, SortOrder = @SortOrder,
                       UpdatedBy = @by, UpdatedAt = GETDATE()
                 WHERE CompanyID = @companyId AND FormKey = @formKey AND FieldKey = @fieldKey;
            ELSE
                INSERT INTO dbo.Cfg_FormField (CompanyID, FormKey, FieldKey, IsVisible, IsRequired, IsReadOnly,
                       MinLength, MaxLength, Pattern, LabelText, Placeholder, HelpText, SortOrder, UpdatedBy)
                VALUES (@companyId, @formKey, @fieldKey, @IsVisible, @IsRequired, @IsReadOnly,
                       @MinLength, @MaxLength, @Pattern, @LabelText, @Placeholder, @HelpText, @SortOrder, @by);
            """, new
        {
            companyId, formKey, fieldKey = rule.FieldKey, rule.IsVisible, rule.IsRequired, rule.IsReadOnly,
            rule.MinLength, rule.MaxLength, rule.Pattern, rule.LabelText, rule.Placeholder, rule.HelpText, rule.SortOrder, by,
        }, tx);
        await LogAndTouchAsync(c, tx, companyId, "formfield", $"{formKey}/{rule.FieldKey}",
                               old is null ? "insert" : "update", old?.Describe(), newText, by);
        await tx.CommitAsync(ct);
    }

    public async Task ResetFormFieldAsync(int companyId, string formKey, string fieldKey, string by, CancellationToken ct = default)
    {
        var p = new { companyId, formKey, fieldKey };
        await using var c = await _db.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        var old = await c.QueryFirstOrDefaultAsync<FieldRule>(
            "SELECT * FROM dbo.Cfg_FormField WHERE CompanyID = @companyId AND FormKey = @formKey AND FieldKey = @fieldKey;", p, tx);
        if (old is null) { await tx.CommitAsync(ct); return; }
        await c.ExecuteAsync("DELETE FROM dbo.Cfg_FormField WHERE CompanyID = @companyId AND FormKey = @formKey AND FieldKey = @fieldKey;", p, tx);
        await LogAndTouchAsync(c, tx, companyId, "formfield", $"{formKey}/{fieldKey}", "delete", old.Describe(), null, by);
        await tx.CommitAsync(ct);
    }

    // ---- branding ------------------------------------------------------------

    /// <summary>A company's branding. Null = not set (All companies, else as today).</summary>
    public sealed class Branding
    {
        public int CompanyID { get; init; }
        public string? LogoUrl { get; init; }
        public string? FaviconUrl { get; init; }
        public string? PrimaryColor { get; init; }
        public string? SecondaryColor { get; init; }
        public string? LoginBgUrl { get; init; }
        public string? LoginTagline { get; init; }
        public string? FooterText { get; init; }
        public string? CopyrightText { get; init; }
        public string? UpdatedBy { get; init; }
        public DateTime UpdatedAt { get; init; }

        public bool IsEmpty => LogoUrl is null && FaviconUrl is null && PrimaryColor is null && SecondaryColor is null
            && LoginBgUrl is null && LoginTagline is null && FooterText is null && CopyrightText is null;

        public string Describe()
        {
            var p = new List<string>();
            if (LogoUrl is not null) p.Add($"Logo: {LogoUrl}");
            if (FaviconUrl is not null) p.Add($"Icon: {FaviconUrl}");
            if (PrimaryColor is not null) p.Add($"Main colour {PrimaryColor}");
            if (SecondaryColor is not null) p.Add($"Second colour {SecondaryColor}");
            if (LoginBgUrl is not null) p.Add($"Login background: {LoginBgUrl}");
            if (LoginTagline is not null) p.Add($"Login line: {LoginTagline}");
            if (FooterText is not null) p.Add($"Footer: {FooterText}");
            if (CopyrightText is not null) p.Add($"Copyright: {CopyrightText}");
            return p.Count == 0 ? "as today" : string.Join(" · ", p);
        }
    }

    /// <summary>(own, All companies) - either may be null.</summary>
    public async Task<(Branding? Own, Branding? Inherited)> BrandingAsync(int companyId, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        var rows = (await c.QueryAsync<Branding>("""
            SELECT CompanyID, LogoUrl, FaviconUrl, PrimaryColor, SecondaryColor, LoginBgUrl,
                   LoginTagline, FooterText, CopyrightText, UpdatedBy, UpdatedAt
              FROM dbo.Cfg_Branding WHERE CompanyID IN (@companyId, 0);
            """, new { companyId })).AsList();
        return (rows.FirstOrDefault(r => r.CompanyID == companyId),
                companyId == 0 ? null : rows.FirstOrDefault(r => r.CompanyID == 0));
    }

    public async Task SetBrandingAsync(int companyId, Branding b, string by, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        var old = await c.QueryFirstOrDefaultAsync<Branding>(
            "SELECT * FROM dbo.Cfg_Branding WHERE CompanyID = @companyId;", new { companyId }, tx);

        if (b.IsEmpty)
        {
            if (old is null) { await tx.CommitAsync(ct); return; }
            await c.ExecuteAsync("DELETE FROM dbo.Cfg_Branding WHERE CompanyID = @companyId;", new { companyId }, tx);
            await LogAndTouchAsync(c, tx, companyId, "branding", "branding", "delete", old.Describe(), null, by);
            await tx.CommitAsync(ct);
            return;
        }

        var newText = b.Describe();
        if (old is not null && old.Describe() == newText) { await tx.CommitAsync(ct); return; }

        await c.ExecuteAsync("""
            IF EXISTS (SELECT 1 FROM dbo.Cfg_Branding WHERE CompanyID = @companyId)
                UPDATE dbo.Cfg_Branding
                   SET LogoUrl = @LogoUrl, FaviconUrl = @FaviconUrl, PrimaryColor = @PrimaryColor,
                       SecondaryColor = @SecondaryColor, LoginBgUrl = @LoginBgUrl, LoginTagline = @LoginTagline,
                       FooterText = @FooterText, CopyrightText = @CopyrightText, UpdatedBy = @by, UpdatedAt = GETDATE()
                 WHERE CompanyID = @companyId;
            ELSE
                INSERT INTO dbo.Cfg_Branding (CompanyID, LogoUrl, FaviconUrl, PrimaryColor, SecondaryColor,
                       LoginBgUrl, LoginTagline, FooterText, CopyrightText, UpdatedBy)
                VALUES (@companyId, @LogoUrl, @FaviconUrl, @PrimaryColor, @SecondaryColor,
                       @LoginBgUrl, @LoginTagline, @FooterText, @CopyrightText, @by);
            """, new
        {
            companyId, b.LogoUrl, b.FaviconUrl, b.PrimaryColor, b.SecondaryColor,
            b.LoginBgUrl, b.LoginTagline, b.FooterText, b.CopyrightText, by,
        }, tx);
        await LogAndTouchAsync(c, tx, companyId, "branding", "branding", old is null ? "insert" : "update", old?.Describe(), newText, by);
        await tx.CommitAsync(ct);
    }

    // ---- plumbing ----------------------------------------------------------

    private async Task UpsertAsync(int companyId, string area, string key, string by, CancellationToken ct,
                                   string select, string upsert, object args, string newValue)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        var old = await c.ExecuteScalarAsync<string?>(select, new { companyId, key }, tx);
        if (old == newValue) { await tx.CommitAsync(ct); return; }   // nothing changed

        await c.ExecuteAsync(upsert, args, tx);
        await LogAndTouchAsync(c, tx, companyId, area, key, old is null ? "insert" : "update", old, newValue, by);
        await tx.CommitAsync(ct);
    }

    private async Task DeleteAsync(int companyId, string area, string key, string by, CancellationToken ct,
                                   string select, string delete)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        var old = await c.ExecuteScalarAsync<string?>(select, new { companyId, key }, tx);
        if (old is null) { await tx.CommitAsync(ct); return; }

        await c.ExecuteAsync(delete, new { companyId, key }, tx);
        await LogAndTouchAsync(c, tx, companyId, area, key, "delete", old, null, by);
        await tx.CommitAsync(ct);
    }

    /// <summary>Change log + version bump, inside the caller's transaction (used by other stores too).</summary>
    public static Task LogAsync(SqlConnection c, SqlTransaction tx, int companyId, string area,
                                string key, string action, string? oldValue, string? newValue, string by) =>
        LogAndTouchAsync(c, tx, companyId, area, key, action, oldValue, newValue, by);

    private static async Task LogAndTouchAsync(SqlConnection c, SqlTransaction tx, int companyId, string area,
                                               string key, string action, string? oldValue, string? newValue, string by)
    {
        await c.ExecuteAsync("""
            INSERT INTO dbo.Cfg_ChangeLog (CompanyID, Area, ItemKey, Action, OldValue, NewValue, ChangedBy)
            VALUES (@companyId, @area, @key, @action, @oldValue, @newValue, @by);
            """, new { companyId, area, key, action, oldValue, newValue, by }, tx);
        await c.ExecuteAsync("EXEC dbo.Cfg_Touch @CompanyID = @companyId;", new { companyId }, tx);
    }
}
