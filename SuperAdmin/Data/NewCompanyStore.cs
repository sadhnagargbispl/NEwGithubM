using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace SuperAdmin.Data;

/// <summary>
/// Starts a NEW company from an existing one. The company's database is made outside
/// (a copy of the source company's database, on the same server); this adds everything
/// the master needs so the panels find it:
///   1. its row in M_CompanyMasterNew (copied from the source, new ID / name / URL);
///   2. its connection - the rows of the master tables Proc_GetConnection1 reads, copied
///      from the source with the database name changed to the new copy;
///   3. optionally the source's panel menus and Super Admin settings.
/// Nothing is guessed silently: Plan() shows every row that will be added, and Create()
/// writes all of it in ONE transaction which it only commits after Proc_GetConnection1
/// returns the new database for the new ID.
/// </summary>
public sealed partial class NewCompanyStore
{
    private readonly Db _db;
    public NewCompanyStore(Db db) => _db = db;

    public sealed record Request(int SourceId, int? NewId, string Name, string Url, string NewDatabase, bool CopyMenus, bool CopySettings, bool MakeCopy = false);

    /// <summary>What "make the database copy for me" will do (filled by Plan when MakeCopy).</summary>
    public sealed class DbCopy
    {
        public long SizeMb { get; set; }
        public string BackupFile { get; set; } = "";
        public List<(string Logical, string Type, string NewPath)> Files { get; } = [];
    }

    public sealed class TableCopy
    {
        public required string Table { get; init; }
        public required string Why { get; init; }
        public int Rows { get; set; }
        public List<string> Changes { get; } = [];      // "Name: old → new"
    }

    public sealed class Plan
    {
        public List<string> Errors { get; } = [];
        public List<string> Notes { get; } = [];
        public int NewId { get; set; }
        public bool IdIsAuto { get; set; }
        public string? SourceName { get; set; }
        public string? SourceDatabase { get; set; }
        public string? SourceUrl { get; set; }
        public List<TableCopy> Copies { get; } = [];
        public DbCopy? Copy { get; set; }
        internal string? SourceConnection { get; set; }
        public bool Ok => Errors.Count == 0;

        // Work prepared by Plan() for Create() - never shown.
        internal List<(string Table, List<Dictionary<string, object?>> Rows)> RegistryRows { get; } = [];
        internal List<(string Table, string KeyColumn)> BulkCopies { get; } = [];
    }

    /// <summary>Master tables the panels key by CompanyID that a new company can start from.</summary>
    private static readonly string[] SettingsTables =
        ["Cfg_Label", "Cfg_ValueType", "Cfg_Feature", "Cfg_FormField", "Cfg_Branding", "Cfg_PageRight"];

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{1,128}$")] private static partial Regex DbNameRx();
    [GeneratedRegex(@"^(CompID|CompanyID|Comp_ID|Company_ID)$", RegexOptions.IgnoreCase)] private static partial Regex CompanyKeyRx();
    [GeneratedRegex(@"(?i)((?:Initial Catalog|Database)\s*=\s*)([^;]*)")] private static partial Regex CatalogRx();
    [GeneratedRegex(@"^[^\s'""<>;]{1,}$")] private static partial Regex UrlRx();

    public async Task<IReadOnlyList<(int Id, string Name)>> SourcesAsync(CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        return (await c.QueryAsync<(int, string)>(
            "SELECT ID, ISNULL(Name, '') FROM dbo.M_CompanyMasterNew ORDER BY CASE WHEN IsActive = 1 THEN 0 ELSE 1 END, Name;")).AsList();
    }

    public async Task<int> SuggestIdAsync(CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>("SELECT ISNULL(MAX(ID), 0) + 1 FROM dbo.M_CompanyMasterNew;");
    }

    /// <summary>Checks the request and works out every row that would be added. Writes nothing.</summary>
    public async Task<Plan> PlanAsync(Request r, CancellationToken ct = default)
    {
        var plan = new Plan();
        var name = (r.Name ?? "").Trim();
        var url = (r.Url ?? "").Trim();
        var newDb = (r.NewDatabase ?? "").Trim();

        await using var m = await _db.OpenAsync(ct);

        // ---- source company + its database ------------------------------------
        var src = (IDictionary<string, object?>?)await m.QueryFirstOrDefaultAsync(
            "SELECT * FROM dbo.M_CompanyMasterNew WHERE ID = @id;", new { id = r.SourceId });
        if (src is null) { plan.Errors.Add("Choose the company to copy from."); return plan; }
        plan.SourceName = src.TryGetValue("Name", out var sn) ? sn?.ToString() : null;
        plan.SourceUrl = src.TryGetValue("URL", out var su) ? su?.ToString() : null;

        string? srcCs = null;
        try
        {
            var row = (IDictionary<string, object?>?)await m.QueryFirstOrDefaultAsync(
                "EXEC dbo.Proc_GetConnection1 @CompID;", new { CompID = r.SourceId.ToString() });
            srcCs = row is not null && row.TryGetValue("ConnectionString", out var v) ? v?.ToString() : null;
        }
        catch (SqlException ex) { plan.Errors.Add("Could not ask the master for the source company's database: " + ex.Message); return plan; }
        if (string.IsNullOrWhiteSpace(srcCs)) { plan.Errors.Add($"Company {r.SourceId} has no database connection in the master, so it cannot be copied from."); return plan; }
        plan.SourceDatabase = new SqlConnectionStringBuilder(srcCs).InitialCatalog;
        if (string.IsNullOrWhiteSpace(plan.SourceDatabase)) { plan.Errors.Add("The source company's connection has no database name."); return plan; }

        // ---- the form -----------------------------------------------------------
        var cols = await ColumnsAsync(m, "M_CompanyMasterNew");
        plan.IdIsAuto = cols.TryGetValue("ID", out var idCol) && idCol.IsIdentity;
        if (!plan.IdIsAuto)
        {
            if (r.NewId is not > 0) plan.Errors.Add("Give the new company an ID (a number).");
            else plan.NewId = r.NewId.Value;
        }
        if (name.Length == 0) plan.Errors.Add("Give the new company a name.");
        else if (cols.TryGetValue("Name", out var nc) && nc.Max > 0 && name.Length > nc.Max) plan.Errors.Add($"Name is too long (most {nc.Max} characters).");
        if (cols.ContainsKey("URL"))
        {
            if (url.Length == 0) plan.Errors.Add("Give the website address the panels open on - the panels find the company by it.");
            else if (!UrlRx().IsMatch(url)) plan.Errors.Add("Website address must not contain spaces, quotes or < > ;.");
            else if (cols["URL"].Max > 0 && url.Length > cols["URL"].Max) plan.Errors.Add($"Website address is too long (most {cols["URL"].Max} characters).");
            else if (await m.ExecuteScalarAsync<int>(
                         "SELECT COUNT(*) FROM dbo.M_CompanyMasterNew WHERE UPPER(LTRIM(RTRIM(URL))) = UPPER(@url);", new { url }) > 0)
                plan.Errors.Add($"Another company already uses the website address “{url}”.");
        }
        if (!DbNameRx().IsMatch(newDb)) plan.Errors.Add("Give the name of the new company's database (letters, digits, _ - . only).");
        else if (string.Equals(newDb, plan.SourceDatabase, StringComparison.OrdinalIgnoreCase))
            plan.Errors.Add("The new company needs its OWN database - this is the source company's database.");
        if (!plan.IdIsAuto && plan.NewId > 0 && await IdInUseAsync(m, plan.NewId) is string used)
            plan.Errors.Add($"ID {plan.NewId} is already used ({used}).");
        if (!plan.Ok) return plan;

        plan.SourceConnection = srcCs;
        if (r.MakeCopy)
        {
            // ---- Super Admin will make the copy: check it can, and where the files go --
            await PlanDbCopyAsync(plan, srcCs, newDb, ct);
        }
        else
        {
            // ---- the new database must already exist (copied from the source) --------
            var target = new SqlConnectionStringBuilder(srcCs) { InitialCatalog = newDb, ConnectTimeout = 30, ApplicationName = "Super Admin" };
            if (!srcCs.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase)) target.TrustServerCertificate = true;
            try
            {
                await using var t = new SqlConnection(target.ConnectionString);
                await t.OpenAsync(ct);
                if (await t.ExecuteScalarAsync<int?>("SELECT OBJECT_ID('dbo.M_CompanyMaster');") is null)
                    plan.Errors.Add($"Database “{newDb}” opens but has no M_CompanyMaster table - is it a copy of {plan.SourceDatabase}?");
            }
            catch (SqlException ex)
            {
                plan.Errors.Add($"Database “{newDb}” cannot be opened with company {r.SourceId}'s login. Make the copy of “{plan.SourceDatabase}” on the same server first, or tick “Make the database copy for me”. ({ex.Message})");
            }
        }
        if (!plan.Ok) return plan;

        // ---- 1. company row -----------------------------------------------------
        var companyRow = new Dictionary<string, object?>(src, StringComparer.OrdinalIgnoreCase);
        var copyMain = new TableCopy { Table = "M_CompanyMasterNew", Why = "The company list - every panel finds the company here", Rows = 1 };
        if (!plan.IdIsAuto) Set(companyRow, copyMain, "ID", plan.NewId);
        Set(companyRow, copyMain, "Name", name);
        if (cols.ContainsKey("URL")) Set(companyRow, copyMain, "URL", url);
        if (cols.ContainsKey("Logo") && !string.IsNullOrEmpty(companyRow["Logo"]?.ToString()))
        {
            Set(companyRow, copyMain, "Logo", cols["Logo"].Nullable ? null : "");
            plan.Notes.Add("The source company's logo is not copied - set the new logo in Branding.");
        }
        if (cols.ContainsKey("IsActive")) Set(companyRow, copyMain, "IsActive", true);
        ReplaceDatabase(companyRow, cols, copyMain, plan.SourceDatabase, newDb, plan);
        plan.RegistryRows.Add(("M_CompanyMasterNew", [companyRow]));
        plan.Copies.Add(copyMain);
        var kept = cols.Keys.Where(k => !copyMain.Changes.Any(ch => ch.StartsWith(k + ":", StringComparison.OrdinalIgnoreCase))
                                        && !cols[k].IsIdentity && !cols[k].Skip)
                            .Except(["ID"], StringComparer.OrdinalIgnoreCase).ToList();
        if (kept.Count > 0) plan.Notes.Add($"Copied unchanged from {plan.SourceName}: {string.Join(", ", kept)}.");

        // ---- 2. connection: the other master tables Proc_GetConnection1 reads ------
        var procTables = (await m.QueryAsync<string>("""
            SELECT DISTINCT o.name
              FROM sys.sql_expression_dependencies d
              JOIN sys.objects o ON o.object_id = d.referenced_id AND o.type = 'U' AND SCHEMA_NAME(o.schema_id) = 'dbo'
             WHERE d.referencing_id = OBJECT_ID('dbo.Proc_GetConnection1') AND d.referenced_database_name IS NULL;
            """)).Where(t => !t.Equals("M_CompanyMasterNew", StringComparison.OrdinalIgnoreCase)
                          && !t.StartsWith("Cfg_", StringComparison.OrdinalIgnoreCase)
                          && !t.StartsWith("SA_", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var table in procTables)
        {
            var tc = await ColumnsAsync(m, table);
            var key = tc.Keys.FirstOrDefault(k => CompanyKeyRx().IsMatch(k));
            if (key is null || tc[key].IsIdentity)
            {
                plan.Errors.Add($"Proc_GetConnection1 reads table {table}, but it has no CompID / CompanyID column, so Super Admin cannot tell which row is the company's. Add this company's connection there by hand.");
                continue;
            }
            var rows = (await m.QueryAsync($"SELECT * FROM dbo.{Q(table)} WHERE {Q(key)} = @id;", new { id = r.SourceId }))
                       .Select(x => new Dictionary<string, object?>((IDictionary<string, object?>)x, StringComparer.OrdinalIgnoreCase)).ToList();
            if (rows.Count == 0) { plan.Notes.Add($"{table}: the source company has no row there - nothing to copy."); continue; }
            if (!plan.IdIsAuto && await m.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{Q(table)} WHERE {Q(key)} = @id;", new { id = plan.NewId }) > 0)
            {
                plan.Errors.Add($"{table} already has rows for ID {plan.NewId}.");
                continue;
            }
            var copy = new TableCopy { Table = table, Why = "Connection - where the panels find the company's database", Rows = rows.Count };
            foreach (var row in rows)
            {
                var one = new TableCopy { Table = table, Why = "" };
                Set(row, one, key, plan.IdIsAuto ? 0 : plan.NewId);
                ReplaceDatabase(row, tc, one, plan.SourceDatabase, newDb, plan);
                foreach (var ch in one.Changes.Where(ch => !copy.Changes.Contains(ch))) copy.Changes.Add(ch);
            }
            plan.RegistryRows.Add((table, rows));
            plan.Copies.Add(copy);
        }

        // ---- 3. menus + settings -------------------------------------------------
        if (r.CopyMenus)
            foreach (var p in MenuStore.Panels)
                await AddBulkAsync(m, plan, p.CompanyTable, "CompanyID", r.SourceId, $"{p.Name} - copied from {plan.SourceName}");
        if (r.CopySettings)
            foreach (var t in SettingsTables)
                await AddBulkAsync(m, plan, t, "CompanyID", r.SourceId, $"Super Admin settings - copied from {plan.SourceName}");
        if (!r.CopyMenus) plan.Notes.Add("Menus are not copied: the panels will show NO menu for the new company until menus are added in Menus.");

        plan.Notes.Add($"The database “{newDb}” still has {plan.SourceName}'s name, address and GST in M_CompanyMaster - change them in Company details right after.");
        return plan;
    }

    /// <summary>Writes the plan in one master transaction; commits only if the panels' lookup then finds the new database.</summary>
    public async Task<(Plan Plan, int NewId)> CreateAsync(Request r, string by, CancellationToken ct = default)
    {
        if (!r.MakeCopy) return await RegisterAsync(r, by, ct);

        var copyPlan = await PlanAsync(r, ct);
        if (!copyPlan.Ok) return (copyPlan, 0);
        var newDb = r.NewDatabase.Trim();
        try
        {
            await MakeDbCopyAsync(copyPlan, newDb, CancellationToken.None);   // never abandon a half-made copy
        }
        catch (SqlException ex)
        {
            copyPlan.Errors.Add("The database copy failed - nothing was added: " + ex.Message);
            // A failed RESTORE can leave the new name stuck in RESTORING; remove only that (never a working database).
            try
            {
                await using var s = ServerConnection(copyPlan.SourceConnection!, "master");
                if (await s.ExecuteScalarAsync<string?>("SELECT state_desc FROM sys.databases WHERE name = @n;", new { n = newDb }) == "RESTORING")
                    await DropOurCopyAsync(copyPlan, newDb);
            }
            catch (SqlException) { /* reported above */ }
            return (copyPlan, 0);
        }

        var (plan, newId) = await RegisterAsync(r with { MakeCopy = false }, by, ct);
        if (newId == 0) { plan.SourceConnection ??= copyPlan.SourceConnection; await DropOurCopyAsync(plan, newDb); }
        else plan.Copy = copyPlan.Copy;
        return (plan, newId);
    }

    private async Task<(Plan Plan, int NewId)> RegisterAsync(Request r, string by, CancellationToken ct)
    {
        var plan = await PlanAsync(r, ct);
        if (!plan.Ok) return (plan, 0);
        var newDb = r.NewDatabase.Trim();

        await using var m = await _db.OpenAsync(ct);
        await using var tx = m.BeginTransaction();
        try
        {
            var newId = plan.NewId;
            if (!plan.IdIsAuto
                && await m.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.M_CompanyMasterNew WITH (UPDLOCK, HOLDLOCK) WHERE ID = @newId;", new { newId }, tx) > 0)
            {
                plan.Errors.Add($"ID {newId} was just taken - choose another.");
                return (plan, 0);
            }

            foreach (var (table, rows) in plan.RegistryRows)
            {
                var cols = await ColumnsAsync(m, table, tx);
                foreach (var row in rows)
                {
                    if (table == "M_CompanyMasterNew" && plan.IdIsAuto) row.Remove("ID");
                    else if (plan.IdIsAuto)
                    {
                        var key = cols.Keys.First(k => CompanyKeyRx().IsMatch(k));
                        row[key] = newId;
                    }
                    var names = row.Keys.Where(k => cols.TryGetValue(k, out var ci) && !ci.IsIdentity && !ci.Skip).ToList();
                    var p = new DynamicParameters();
                    for (var i = 0; i < names.Count; i++) p.Add($"p{i}", row[names[i]]);
                    var sql = $"INSERT INTO dbo.{Q(table)} ({string.Join(", ", names.Select(Q))}) "
                            + (table == "M_CompanyMasterNew" && plan.IdIsAuto ? "OUTPUT CAST(inserted.ID AS int) " : "")
                            + $"VALUES ({string.Join(", ", names.Select((_, i) => $"@p{i}"))});";
                    if (table == "M_CompanyMasterNew" && plan.IdIsAuto) newId = await m.ExecuteScalarAsync<int>(sql, p, tx);
                    else await m.ExecuteAsync(sql, p, tx);
                }
            }

            foreach (var (table, key) in plan.BulkCopies)
            {
                var cols = await ColumnsAsync(m, table, tx);
                var names = cols.Where(x => !x.Value.IsIdentity && !x.Value.Skip).Select(x => x.Key).ToList();
                var select = names.Select(n => n.Equals(key, StringComparison.OrdinalIgnoreCase) ? "@newId" : Q(n));
                await m.ExecuteAsync($"INSERT INTO dbo.{Q(table)} ({string.Join(", ", names.Select(Q))}) SELECT {string.Join(", ", select)} FROM dbo.{Q(table)} WHERE {Q(key)} = @src;",
                    new { newId, src = r.SourceId }, tx);
            }

            // The proof: the panels' own lookup must now give the new database for the new ID.
            var check = (IDictionary<string, object?>?)await m.QueryFirstOrDefaultAsync(
                "EXEC dbo.Proc_GetConnection1 @CompID;", new { CompID = newId.ToString() }, tx);
            var got = check is not null && check.TryGetValue("ConnectionString", out var v) ? v?.ToString() : null;
            var gotDb = string.IsNullOrWhiteSpace(got) ? null : new SqlConnectionStringBuilder(got).InitialCatalog;
            if (!string.Equals(gotDb, newDb, StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync(ct);
                plan.Errors.Add($"Nothing was saved: after adding the rows, Proc_GetConnection1 '{newId}' gives database “{gotDb ?? "(none)"}” instead of “{newDb}”. The connection is kept somewhere Super Admin does not know - it has to be added by hand.");
                return (plan, 0);
            }

            await ConfigStore.LogAsync(m, tx, newId, "company", "company", "create", null,
                $"{r.Name.Trim()} · database {newDb} · copied from {r.SourceId} {plan.SourceName}"
                + (r.CopyMenus ? " · menus" : "") + (r.CopySettings ? " · settings" : ""), by);
            await tx.CommitAsync(ct);
            return (plan, newId);
        }
        catch (SqlException ex)
        {
            try { await tx.RollbackAsync(ct); } catch { /* already rolled back */ }
            plan.Errors.Add("Nothing was saved - the master database refused: " + ex.Message);
            return (plan, 0);
        }
    }

    // ---- database copy (backup → restore under the new name) ---------------------

    private static SqlConnection ServerConnection(string srcCs, string database)
    {
        var b = new SqlConnectionStringBuilder(srcCs) { InitialCatalog = database, ConnectTimeout = 30, ApplicationName = "Super Admin" };
        if (!srcCs.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase)) b.TrustServerCertificate = true;
        return new SqlConnection(b.ConnectionString);
    }

    /// <summary>Checks Super Admin can copy the source database on its server and works out the new file names. Writes nothing.</summary>
    private static async Task PlanDbCopyAsync(Plan plan, string srcCs, string newDb, CancellationToken ct)
    {
        var srcDb = plan.SourceDatabase!;
        try
        {
            await using var s = ServerConnection(srcCs, srcDb);
            await s.OpenAsync(ct);
            if (await s.ExecuteScalarAsync<int?>("SELECT DB_ID(@newDb);", new { newDb }) is not null)
            {
                plan.Errors.Add($"A database “{newDb}” already exists on that server. Use another name, or untick “Make the database copy for me” to use the existing one.");
                return;
            }
            if (await s.ExecuteScalarAsync<int>("SELECT ISNULL(HAS_PERMS_BY_NAME(NULL, NULL, 'CREATE ANY DATABASE'), 0);") != 1)
                plan.Errors.Add($"Company {srcDb}'s SQL login may not create databases (it needs the dbcreator role). Ask the DBA, or make the copy by hand.");
            if (await s.ExecuteScalarAsync<int>("SELECT ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'BACKUP DATABASE'), 0);") != 1)
                plan.Errors.Add($"Company {srcDb}'s SQL login may not back up {srcDb} (it needs db_backupoperator). Ask the DBA, or make the copy by hand.");
            if (await s.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.database_files WHERE type NOT IN (0, 1);") > 0)
                plan.Errors.Add($"{srcDb} has full-text or FILESTREAM files - Super Admin does not copy those. Make the copy by hand.");
            if (!plan.Ok) return;

            var copy = new DbCopy { SizeMb = await s.ExecuteScalarAsync<long>("SELECT CAST(SUM(CAST(size AS bigint)) * 8 / 1024 AS bigint) FROM sys.database_files;") };
            var paths = await s.QueryFirstAsync<(string? Data, string? Log, string? Backup, string? SrcFile)>("""
                SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(400)),
                       CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(400)),
                       CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(400)),
                       (SELECT TOP 1 physical_name FROM sys.database_files WHERE type = 0 ORDER BY file_id);
                """);
            var srcDir = string.IsNullOrEmpty(paths.SrcFile) ? null : paths.SrcFile[..(paths.SrcFile.LastIndexOfAny(['\\', '/']) + 1)];
            string Dir(string? p) => string.IsNullOrWhiteSpace(p) ? srcDir ?? "" : p.EndsWith('\\') || p.EndsWith('/') ? p : p + "\\";
            var dataDir = Dir(srcDir ?? paths.Data);   // next to the source's files: same drive, known to be writable
            var logDir = Dir(paths.Log ?? srcDir);
            var bakDir = Dir(paths.Backup ?? srcDir);
            if (dataDir.Length == 0) { plan.Errors.Add("Could not find the SQL server's data folder. Make the copy by hand."); return; }

            copy.BackupFile = $"{bakDir}SuperAdmin-copy-{srcDb}-to-{newDb}-{DateTime.Now:yyyyMMddHHmmss}.bak";
            var files = (await s.QueryAsync<(string Name, byte Type)>("SELECT name, CAST(type AS tinyint) FROM sys.database_files ORDER BY file_id;")).ToList();
            int d = 0, l = 0;
            foreach (var (name, type) in files)
            {
                var path = type == 1
                    ? $"{logDir}{newDb}_log{(l++ == 0 ? "" : l.ToString())}.ldf"
                    : $"{dataDir}{newDb}{(d++ == 0 ? ".mdf" : $"_{d}.ndf")}";
                copy.Files.Add((name, type == 1 ? "log" : "data", path));
            }
            plan.Copy = copy;
            plan.Notes.Insert(0, $"Super Admin will copy database “{srcDb}” ({copy.SizeMb:N0} MB) to “{newDb}”. It is a FULL copy - it includes all of {plan.SourceName}'s members, orders and payouts. "
                               + $"A backup file is left at {copy.BackupFile} (delete it later on the server).");
        }
        catch (SqlException ex) { plan.Errors.Add($"Could not check the source database's server: {ex.Message}"); }
    }

    private static async Task MakeDbCopyAsync(Plan plan, string newDb, CancellationToken ct)
    {
        var c = plan.Copy!;
        await using var s = ServerConnection(plan.SourceConnection!, "master");
        await s.OpenAsync(ct);
        await s.ExecuteAsync($"BACKUP DATABASE {Q(plan.SourceDatabase!)} TO DISK = @f WITH COPY_ONLY, INIT, CHECKSUM;", new { f = c.BackupFile }, commandTimeout: 0);
        var p = new DynamicParameters();
        p.Add("f", c.BackupFile);
        var moves = c.Files.Select((x, i) => { p.Add($"l{i}", x.Logical); p.Add($"p{i}", x.NewPath); return $"MOVE @l{i} TO @p{i}"; });
        // never REPLACE: if anything with that name appeared meanwhile, the restore fails instead of overwriting it
        await s.ExecuteAsync($"RESTORE DATABASE {Q(newDb)} FROM DISK = @f WITH {string.Join(", ", moves)}, CHECKSUM, RECOVERY;", p, commandTimeout: 0);
    }

    private static async Task DropOurCopyAsync(Plan plan, string newDb)
    {
        try
        {
            await using var s = ServerConnection(plan.SourceConnection!, "master");
            await s.OpenAsync();
            await s.ExecuteAsync($"IF DB_ID(@n) IS NOT NULL BEGIN ALTER DATABASE {Q(newDb)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Q(newDb)}; END", new { n = newDb });
            plan.Notes.Add($"The database copy “{newDb}” made just now was removed again.");
        }
        catch (SqlException ex) { plan.Errors.Add($"The database copy “{newDb}” made just now could not be removed ({ex.Message}) - remove it on the server before trying again."); }
    }

    // ---- helpers -----------------------------------------------------------------

    private sealed record Col(bool IsIdentity, bool Skip, bool Nullable, bool IsText, int Max);

    private static async Task<Dictionary<string, Col>> ColumnsAsync(SqlConnection c, string table, SqlTransaction? tx = null) =>
        (await c.QueryAsync<(string Name, bool Ident, bool Skip, bool Null, bool Text, int Max)>("""
            SELECT c.name, c.is_identity,
                   CAST(CASE WHEN c.is_computed = 1 OR t.name IN ('timestamp', 'rowversion') THEN 1 ELSE 0 END AS bit),
                   c.is_nullable,
                   CAST(CASE WHEN t.name IN ('varchar', 'nvarchar', 'char', 'nchar', 'text', 'ntext') THEN 1 ELSE 0 END AS bit),
                   CASE WHEN c.max_length = -1 THEN -1 WHEN t.name IN ('nvarchar', 'nchar') THEN c.max_length / 2 ELSE c.max_length END
              FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
             WHERE c.object_id = OBJECT_ID('dbo.' + @table);
            """, new { table }, tx)).ToDictionary(x => x.Name, x => new Col(x.Ident, x.Skip, x.Null, x.Text, x.Max), StringComparer.OrdinalIgnoreCase);

    private static void Set(Dictionary<string, object?> row, TableCopy copy, string column, object? value)
    {
        var name = row.Keys.FirstOrDefault(k => k.Equals(column, StringComparison.OrdinalIgnoreCase)) ?? column;
        row.TryGetValue(name, out var old);
        row[name] = value;
        if (!Equals(old?.ToString(), value?.ToString())) copy.Changes.Add($"{name}: {Show(old)} → {Show(value)}");
    }

    /// <summary>Points text values at the new database: a value that IS the database name, or a connection string's catalog.</summary>
    private static void ReplaceDatabase(Dictionary<string, object?> row, Dictionary<string, Col> cols, TableCopy copy, string oldDb, string newDb, Plan plan)
    {
        foreach (var k in row.Keys.ToList())
        {
            if (row[k] is not string s || !cols.TryGetValue(k, out var col) || !col.IsText) continue;
            string next;
            if (s.Trim().Equals(oldDb, StringComparison.OrdinalIgnoreCase)) next = newDb;
            else next = CatalogRx().Replace(s, mm => mm.Groups[2].Value.Trim().Equals(oldDb, StringComparison.OrdinalIgnoreCase) ? mm.Groups[1].Value + newDb : mm.Value);
            if (next == s) continue;
            if (col.Max > 0 && next.Length > col.Max) { plan.Errors.Add($"{copy.Table}.{k}: the new database name makes the value longer than {col.Max} characters."); continue; }
            row[k] = next;
            // Connection strings carry the password - show only that the catalog changed.
            copy.Changes.Add(next.Contains('=') ? $"{k}: database {oldDb} → {newDb}" : $"{k}: {Show(s)} → {Show(next)}");
        }
    }

    private async Task AddBulkAsync(SqlConnection m, Plan plan, string table, string key, int src, string why)
    {
        var cols = await ColumnsAsync(m, table);
        if (!cols.ContainsKey(key)) return;   // table not in this master
        var n = await m.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{Q(table)} WHERE {Q(key)} = @src;", new { src });
        if (n == 0) return;
        plan.BulkCopies.Add((table, key));
        plan.Copies.Add(new TableCopy { Table = table, Why = why, Rows = n });
    }

    private static async Task<string?> IdInUseAsync(SqlConnection m, int id)
    {
        if (await m.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.M_CompanyMasterNew WHERE ID = @id;", new { id }) > 0) return "another company has it";
        foreach (var t in MenuStore.Panels.Select(p => p.CompanyTable).Concat(SettingsTables))
            if (await m.ExecuteScalarAsync<int?>("SELECT OBJECT_ID('dbo.' + @t);", new { t }) is not null
                && await m.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{Q(t)} WHERE CompanyID = @id;", new { id }) > 0)
                return $"old rows for it are still in {t}";
        return null;
    }

    private static string Q(string name) => "[" + name.Replace("]", "]]") + "]";
    private static string Show(object? v) => v is null or DBNull ? "(empty)" : v.ToString() is { Length: > 0 } s ? "“" + s + "”" : "(empty)";
}
